using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Testfowm;

// Файловая запись выполняется отдельно от чтения COM-порта и обработки кнопки STOP.
internal sealed class RobotCalibrationJournal : IDisposable
{
    private sealed record Entry(string Kind, DateTimeOffset TimeUtc, RobotState? State,
        bool Connected, string? Label = null, string? Note = null, string? Action = null,
        string? Error = null, TaskCompletionSource<bool>? Saved = null);
    private readonly Channel<Entry> queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(256)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Task worker;
    private volatile string? error;
    private int tracePart = 1;
    private long sequence;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    public string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiSensor", "Calibration",
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
    public string? Error => error;

    public RobotCalibrationJournal() => worker = Task.Run(WriteLoopAsync);

    public void Observe(RobotState state, bool connected) => Enqueue(
        new("state", DateTimeOffset.UtcNow, state, connected));

    public void Connection(bool connected, string? message = null) => Enqueue(
        new("connection", DateTimeOffset.UtcNow, null, connected, Error: message));

    public void Command(string action, string phase, RobotState? state, bool connected, string? message = null) =>
        Enqueue(new("command-" + phase, DateTimeOffset.UtcNow, state, connected, Action: action, Error: message));

    private void Enqueue(Entry entry)
    {
        // При медленном диске движение не ждёт журнала. Пропуск телеметрии виден в интерфейсе.
        if (!queue.Writer.TryWrite(entry)) error = "Журнал занят: часть телеметрии пропущена.";
    }

    public async Task SavePointAsync(RobotState state, string label, string note)
    {
        var saved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new("point", DateTimeOffset.UtcNow, state, true,
            label, note, Saved: saved));
        await saved.Task;
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var entry in queue.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string json = JsonSerializer.Serialize(new
                {
                    sequence = ++sequence, kind = entry.Kind, timeUtc = entry.TimeUtc,
                    connected = entry.Connected, state = entry.State, label = entry.Label,
                    note = entry.Note, action = entry.Action, error = entry.Error,
                    contactConfirmedByUser = entry.Kind == "point" ? (bool?)true : null,
                    anglesAreCommanded = true
                }, JsonOptions);
                string tracePath = Path.Combine(DirectoryPath, $"trace-{tracePart:D3}.jsonl");
                if (File.Exists(tracePath) && new FileInfo(tracePath).Length > 5_000_000)
                    tracePath = Path.Combine(DirectoryPath, $"trace-{++tracePart:D3}.jsonl");
                await File.AppendAllTextAsync(tracePath, json + "\n", Encoding.UTF8);
                if (entry.Kind == "point")
                    await File.AppendAllTextAsync(Path.Combine(DirectoryPath, "points.jsonl"), json + "\n", Encoding.UTF8);
                if (entry.Kind is "state" or "connection")
                {
                    string temporaryPath = Path.Combine(DirectoryPath, "live.tmp");
                    await File.WriteAllTextAsync(temporaryPath, json, Encoding.UTF8);
                    File.Move(temporaryPath, Path.Combine(DirectoryPath, "live.json"), true);
                }
                entry.Saved?.TrySetResult(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = "Ошибка журнала: " + ex.Message;
                entry.Saved?.TrySetException(ex);
            }
        }
    }

    public async Task FinishAsync()
    {
        queue.Writer.TryComplete();
        await worker;
    }

    public void Dispose() => queue.Writer.TryComplete();
}
