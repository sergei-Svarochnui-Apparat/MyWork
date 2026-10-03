using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;

namespace Testfowm;

internal sealed class ConnectArduino : IDisposable
{
    private readonly object writeLock = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> pending = new();
    private SerialPort? port;
    private CancellationTokenSource? cancellation;
    private Task? reader;
    private System.Threading.Timer? heartbeat;
    private TaskCompletionSource<bool>? hello;
    private string helloToken = "";
    private int nextId;
    private volatile bool ready;
    private volatile RobotState? state;
    private volatile string? error;
    private long stateReceivedAt;
    private volatile AuxServoState? auxState;
    private long auxStateReceivedAt;
    public event Action? StateChanged;
    public event Action<RobotState>? StateReceived;
    public event Action<AuxServoState>? AuxStateReceived;
    public AuxServoState? AuxiliaryState => auxState;
    public double AuxiliaryStateAgeMs
    {
        get
        {
            long stamp = Interlocked.Read(ref auxStateReceivedAt);
            return stamp != 0 ? Stopwatch.GetElapsedTime(stamp).TotalMilliseconds : double.PositiveInfinity;
        }
    }

    private void ClearAuxState()
    {
        auxState = null;
        Interlocked.Exchange(ref auxStateReceivedAt, 0);
    }

    public bool IsConnected => ready;
    public bool HasSession => port is not null;
    public RobotState? State => state;
    public string? Error => error;
    public double StateAgeMs
    {
        get
        {
            long stamp = Interlocked.Read(ref stateReceivedAt);
            return stamp != 0 ? Stopwatch.GetElapsedTime(stamp).TotalMilliseconds : double.PositiveInfinity;
        }
    }
    public static string[] GetPorts() => SerialPort.GetPortNames().OrderBy(p => p).ToArray();

    public async Task ConnectAsync(string portName)
    {
        if (port is not null) throw new InvalidOperationException("Порт уже открыт.");
        error = null;
        state = null;
        ClearAuxState();
        Interlocked.Exchange(ref stateReceivedAt, 0);
        ready = false;
        helloToken = Guid.NewGuid().ToString("N");
        hello = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var localPort = new SerialPort(portName, 115200)
        {
            NewLine = "\n", ReadTimeout = 200, WriteTimeout = 200,
            DtrEnable = false, RtsEnable = false
        };
        try
        {
            localPort.Open();
            port = localPort;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            reader = Task.Run(() => ReadLoop(localPort, token));
            for (int attempt = 0; attempt < 4; attempt++)
            {
                WriteLine("HELLO " + helloToken);
                if (await Task.WhenAny(hello.Task, Task.Delay(1000)) == hello.Task)
                {
                    await hello.Task;
                    heartbeat = new System.Threading.Timer(_ => SendHeartbeat(), null, 0, 300);
                    return;
                }
            }
            throw new TimeoutException("ESP32 не ответил AISENSOR 1. Загрузи Firmware/RobotBridge/RobotBridge.ino.");
        }
        catch
        {
            if (port is null) localPort.Dispose();
            await DisconnectAsync();
            throw;
        }
    }

    private void SendHeartbeat()
    {
        if (!ready) return;
        try { WriteLine("PING"); }
        catch (Exception ex) { FailConnection(ex.Message); }
    }

    private void WriteLine(string text)
    {
        lock (writeLock)
        {
            if (port is not { IsOpen: true }) throw new IOException("COM-порт отключён.");
            port.WriteLine(text);
        }
    }

    public async Task SendAsync(string action, TimeSpan? timeout = null)
    {
        if (!ready) throw new InvalidOperationException("Сначала подключи ESP32.");
        if (action.Contains('\n') || action.Contains('\r'))
            throw new ArgumentException("Команда должна занимать одну строку.");
        int id = Interlocked.Increment(ref nextId);
        if (id <= 0) throw new InvalidOperationException("Переподключи порт: исчерпан счётчик команд.");
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completed;
        try
        {
            error = null;
            WriteLine(id.ToString(CultureInfo.InvariantCulture) + " " + action);
            await completed.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Исход команды неизвестен: разрываем сеанс, watchdog остановит дальнейшее движение.
            FailConnection("ESP32 не подтвердил выполнение команды.");
            throw;
        }
        finally { pending.TryRemove(id, out _); }
    }

    private void ReadLoop(SerialPort localPort, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string line;
                try { line = localPort.ReadLine().Trim(); }
                catch (TimeoutException) { continue; }
                if (line.Length > 256) continue;
                if (line == "BOOT AISENSOR 1")
                {
                    state = null;
                    ClearAuxState();
                    if (ready) FailConnection("ESP32 перезапустился. Переподключи порт.");
                }
                else if (line == "READY AISENSOR 1 " + helloToken && hello is { Task.IsCompleted: false })
                {
                    ready = true;
                    hello?.TrySetResult(true);
                }
                else if (AuxServoState.TryParse(line, out var parsedAux))
                {
                    auxState = parsedAux;
                    Interlocked.Exchange(ref auxStateReceivedAt, Stopwatch.GetTimestamp());
                    AuxStateReceived?.Invoke(parsedAux!);
                }
                else if (RobotState.TryParse(line, out var parsed))
                {
                    state = parsed;
                    Interlocked.Exchange(ref stateReceivedAt, Stopwatch.GetTimestamp());
                    StateReceived?.Invoke(parsed!);
                }
                else if (line.StartsWith("DONE ") || line.StartsWith("ERR "))
                {
                    var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.None,
                        CultureInfo.InvariantCulture, out int id) && pending.TryGetValue(id, out var promise))
                    {
                        if (parts[0] == "DONE") promise.TrySetResult(true);
                        else promise.TrySetException(new InvalidOperationException(
                            parts.Length == 3 ? ExplainError(parts[2]) : "ESP32 отклонил команду."));
                    }
                }
                else if (line == "NOTICE WATCHDOG") error = "ESP32 остановлен: связь не подтверждалась.";
                StateChanged?.Invoke();
            }
        }
        catch (Exception ex) when (token.IsCancellationRequested ||
            ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (!token.IsCancellationRequested) FailConnection(ex.Message);
        }
    }

    private static string ExplainError(string code) => code switch
    {
        "LIMIT" => "Команда выходит за тестовый диапазон сустава.",
        "NOT_ARMED" => "Сначала нажми «Включить приводы».",
        "AUX_NOT_ENABLED" => "Сначала включи сервопривод D26.",
        "BUSY" => "Предыдущее движение ещё выполняется.",
        "CANCELLED" => "Движение остановлено.",
        "WATCHDOG" => "Движение остановлено из-за потери связи.",
        _ => "ESP32: " + code
    };

    private void FailConnection(string message)
    {
        ready = false;
        error = message;
        state = null;
        ClearAuxState();
        Interlocked.Exchange(ref stateReceivedAt, 0);
        foreach (var item in pending.Values) item.TrySetException(new IOException(message));
        hello?.TrySetException(new IOException(message));
        StateChanged?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        ready = false;
        heartbeat?.Dispose();
        heartbeat = null;
        cancellation?.Cancel();
        lock (writeLock)
        {
            try { port?.Close(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { }
        }
        if (reader is not null) await reader;
        lock (writeLock)
        {
            try { port?.Dispose(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { }
            port = null;
        }
        cancellation?.Dispose();
        cancellation = null;
        reader = null;
        state = null;
        ClearAuxState();
        Interlocked.Exchange(ref stateReceivedAt, 0);
        foreach (var item in pending.Values) item.TrySetException(new IOException("Порт закрыт."));
        hello = null;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        ready = false;
        ClearAuxState();
        heartbeat?.Dispose();
        cancellation?.Cancel();
        lock (writeLock)
        {
            try { port?.Dispose(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { }
            port = null;
        }
        foreach (var item in pending.Values) item.TrySetException(new IOException("Порт закрыт."));
        cancellation?.Dispose();
    }
}
