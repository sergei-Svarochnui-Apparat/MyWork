using System.Globalization;

namespace Testfowm;

public sealed class RobotControlPanel : UserControl
{
    private readonly ConnectArduino robot = new();
    private readonly RobotCalibrationJournal journal = new();
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 250 };
    private readonly ComboBox pointName = new() { Width = 250, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox pointNote = new() { Width = 280, PlaceholderText = "Как пришёл в позу / замечания" };
    private readonly CheckBox contactConfirmed = new() { Text = "Мышь лежит плоско на коврике", AutoSize = true };
    private readonly Button savePoint = new() { Text = "Запомнить точку", AutoSize = true };
    private readonly Button copyJournalPath = new() { Text = "Путь к записи", AutoSize = true };
    private readonly Label recordingLabel = new() { AutoSize = true };
    private readonly ComboBox ports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly Button connect = new() { Text = "Подключить ESP32", AutoSize = true };
    private readonly Button refresh = new() { Text = "Порты", AutoSize = true };
    private readonly Button arm = new() { Text = "Включить приводы", AutoSize = true };
    private readonly Button stop = new() { Text = "СТОП", AutoSize = true, BackColor = Color.MistyRose };
    private readonly Button home = new() { Text = "Исходная поза", AutoSize = true };
    private readonly NumericUpDown step = new()
    {
        Minimum = .1m, Maximum = 2m, DecimalPlaces = 1, Increment = .1m, Value = .5m, Width = 55
    };
    private readonly Label information = new() { AutoSize = true };
    private readonly Label message = new() { AutoSize = true };
    private readonly List<Button> motionButtons = new();
    private bool busy;
    private bool connecting;
    private bool shuttingDown;
    private bool savingPoint;
    private int savedPoints;
    private RobotState? previousDisplayedState;
    private RobotState? contactConfirmedState;
    private bool? loggedConnection;
    private string? loggedError;

    public RobotControlPanel()
    {
        AutoSize = true;
        Dock = DockStyle.Fill;
        var rows = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(8)
        };
        var connection = new FlowLayoutPanel { AutoSize = true };
        connection.Controls.Add(new Label { Text = "Рука / COM:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        connection.Controls.Add(ports);
        connection.Controls.Add(refresh);
        connection.Controls.Add(connect);
        connection.Controls.Add(arm);
        connection.Controls.Add(stop);
        connection.Controls.Add(home);
        var movement = new FlowLayoutPanel { AutoSize = true };
        movement.Controls.Add(new Label { Text = "Шаг, °:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        movement.Controls.Add(step);
        string[] names = { "Основание", "Плечо", "Локоть" };
        for (int axis = 0; axis < names.Length; axis++)
        {
            int capturedAxis = axis;
            foreach (int sign in new[] { -1, 1 })
            {
                int capturedSign = sign;
                var button = new Button { Text = names[axis] + (sign < 0 ? " −" : " +"), AutoSize = true };
                button.Click += async (_, _) => await ExecuteAsync("STEP " + capturedAxis + " " +
                    ((float)step.Value * capturedSign).ToString("0.0", CultureInfo.InvariantCulture));
                motionButtons.Add(button);
                movement.Controls.Add(button);
            }
        }
        rows.Controls.Add(connection);
        rows.Controls.Add(movement);
        rows.Controls.Add(information);
        rows.Controls.Add(message);
        var calibration = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(1000, 0) };
        pointName.Items.AddRange(new object[]
        {
            "Исходная поза", "X: левый край участка", "X: правый край участка",
            "Y: передний край участка", "Y: задний край участка",
            "Y: вперёд — промежуточная", "Y: назад — промежуточная",
            "Подготовка к движению назад"
        });
        pointName.SelectedIndex = 0;
        calibration.Controls.Add(new Label { Text = "Точка на коврике:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        calibration.Controls.Add(pointName);
        calibration.Controls.Add(pointNote);
        calibration.Controls.Add(contactConfirmed);
        calibration.Controls.Add(savePoint);
        calibration.Controls.Add(copyJournalPath);
        rows.Controls.Add(calibration);
        rows.Controls.Add(recordingLabel);
        Controls.Add(rows);
        refresh.Click += (_, _) => RefreshPorts();
        connect.Click += Connect_Click;
        arm.Click += async (_, _) => await ExecuteAsync("ARM");
        home.Click += async (_, _) => await ExecuteAsync("HOME");
        stop.Click += async (_, _) => await ExecuteAsync("STOP", true);
        robot.StateChanged += ScheduleUpdate;
        robot.StateReceived += RecordState;
        savePoint.Click += SavePoint_Click;
        contactConfirmed.CheckedChanged += (_, _) =>
        {
            contactConfirmedState = contactConfirmed.Checked ? robot.State : null;
            UpdateStatus();
        };
        copyJournalPath.Click += (_, _) =>
        {
            try { Clipboard.SetText(journal.DirectoryPath); message.Text = "Путь к записи скопирован."; }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or System.Threading.ThreadStateException)
            { message.Text = ex.Message; }
        };
        statusTimer.Tick += (_, _) => UpdateStatus();
        statusTimer.Start();
        message.Text = "Первое включение задаёт позу 47° / 110° / 43°. Фактические углы не измеряются.";
        RefreshPorts();
        UpdateStatus();
    }

    private void RefreshPorts()
    {
        string? previous = ports.SelectedItem as string;
        ports.Items.Clear();
        try
        {
            ports.Items.AddRange(ConnectArduino.GetPorts());
            if (previous is not null && ports.Items.Contains(previous)) ports.SelectedItem = previous;
            else if (ports.Items.Contains("COM4")) ports.SelectedItem = "COM4";
            else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
        }
        catch (Exception ex) { message.Text = ex.Message; }
    }

    private async void Connect_Click(object? sender, EventArgs e)
    {
        if (connecting || shuttingDown) return;
        connecting = true;
        UpdateStatus();
        try
        {
            if (robot.HasSession)
            {
                if (robot.IsConnected) await robot.SendAsync("STOP");
                await robot.DisconnectAsync();
                message.Text = "Порт закрыт.";
            }
            else
            {
                if (ports.SelectedItem is not string port) throw new InvalidOperationException("Выбери COM-порт.");
                message.Text = "Подключение…";
                await robot.ConnectAsync(port);
                message.Text = "Прошивка RobotBridge распознана. Приводы включаются отдельной кнопкой.";
            }
        }
        catch (Exception ex)
        {
            message.Text = ex.Message;
            await robot.DisconnectAsync();
        }
        finally { connecting = false; UpdateStatus(); }
    }

    private async Task ExecuteAsync(string action, bool interrupt = false)
    {
        if (shuttingDown || (!interrupt && busy)) return;
        contactConfirmed.Checked = false;
        if (!interrupt) busy = true;
        UpdateStatus();
        try
        {
            journal.Command(action, "sent", robot.State, robot.IsConnected);
            await robot.SendAsync(action);
            journal.Command(action, "done", robot.State, robot.IsConnected);
            message.Text = action == "STOP" ? "Остановлено, последнее положение удерживается." :
                "Выполнено: " + action;
        }
        catch (Exception ex)
        {
            journal.Command(action, "failed", robot.State, robot.IsConnected, ex.Message);
            message.Text = ex.Message;
        }
        finally
        {
            if (!interrupt) busy = false;
            UpdateStatus();
        }
    }

    private void RecordState(RobotState state) => journal.Observe(state, robot.IsConnected);

    private async void SavePoint_Click(object? sender, EventArgs e)
    {
        if (savingPoint || shuttingDown) return;
        var state = robot.State;
        if (!CanSavePoint(state)) { message.Text = "Дождись остановки и подтверди контакт мыши с ковриком."; return; }
        string label = pointName.Text.Trim();
        if (label.Length == 0) { message.Text = "Укажи название точки."; return; }
        savingPoint = true;
        UpdateStatus();
        try
        {
            await journal.SavePointAsync(state!, label, pointNote.Text.Trim());
            savedPoints++;
            message.Text = $"Точка {savedPoints}: {label} — {state!.Base:F1}° / {state.Shoulder:F1}° / {state.Elbow:F1}°.";
            // Это только запись: сохранение не посылает роботу команду движения.
        }
        catch (Exception ex) { message.Text = "Точка не сохранена: " + ex.Message; }
        finally { savingPoint = false; UpdateStatus(); }
    }

    private bool CanSavePoint(RobotState? state) => robot.IsConnected && robot.StateAgeMs < 750 &&
        state is { Armed: true, Moving: false } && contactConfirmed.Checked && state == contactConfirmedState &&
        !busy && !connecting && !shuttingDown && !savingPoint;

    private void ScheduleUpdate()
    {
        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            if (InvokeRequired) BeginInvoke((Action)UpdateStatus);
            else UpdateStatus();
        }
        catch (InvalidOperationException) { }
    }

    private void UpdateStatus()
    {
        if (IsDisposed) return;
        bool linked = robot.IsConnected;
        var state = robot.State;
        bool fresh = robot.StateAgeMs < 750;
        if (state is null || !linked || !fresh || state.Moving ||
            previousDisplayedState is { } previous &&
            (Math.Abs(previous.Base - state.Base) > .01f ||
             Math.Abs(previous.Shoulder - state.Shoulder) > .01f ||
             Math.Abs(previous.Elbow - state.Elbow) > .01f)) contactConfirmed.Checked = false;
        previousDisplayedState = state;
        if (loggedConnection != linked || loggedError != robot.Error)
        {
            loggedConnection = linked;
            loggedError = robot.Error;
            journal.Connection(linked, robot.Error);
        }
        ports.Enabled = refresh.Enabled = !robot.HasSession && !connecting && !shuttingDown;
        connect.Text = robot.HasSession ? "Отключить ESP32" : "Подключить ESP32";
        connect.Enabled = !connecting && !shuttingDown;
        arm.Enabled = linked && fresh && state is { Armed: false } && !busy && !shuttingDown;
        stop.Enabled = linked && !shuttingDown;
        home.Enabled = linked && fresh && state is { Armed: true, Moving: false } && !busy && !shuttingDown;
        foreach (var button in motionButtons) button.Enabled = home.Enabled;
        savePoint.Enabled = CanSavePoint(state);
        contactConfirmed.Enabled = linked && fresh && state is { Armed: true, Moving: false } && !busy && !shuttingDown;
        recordingLabel.Text = $"Запомнено точек: {savedPoints}. Журнал: {Path.GetFileName(journal.DirectoryPath)}" +
            (linked && !fresh ? " — телеметрия устарела" : "") +
            (journal.Error is string journalError ? " — " + journalError : "");
        information.Text = state is null ? "Рука: " + (linked ? "ожидание состояния" : "отключена") :
            $"Командные углы: основание {state.Base:F1}° [{state.BaseMin}…{state.BaseMax}] | " +
            $"плечо {state.Shoulder:F1}° [{state.ShoulderMin}…{state.ShoulderMax}] | " +
            $"локоть {state.Elbow:F1}° [{state.ElbowMin}…{state.ElbowMax}] | " +
            (state.Armed ? (state.Moving ? "движется" : "активна") : "движение заблокировано");
        if (robot.Error is string error) message.Text = error;
    }

    public async Task ShutdownAsync()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        UpdateStatus();
        try
        {
            if (robot.IsConnected) await robot.SendAsync("STOP");
        }
        catch (Exception ex) { message.Text = ex.Message; }
        finally
        {
            await robot.DisconnectAsync();
            statusTimer.Stop();
            journal.Connection(false, "Приложение закрыто.");
            await journal.FinishAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            robot.StateChanged -= ScheduleUpdate;
            robot.StateReceived -= RecordState;
            statusTimer.Dispose();
            robot.Dispose();
            journal.Dispose();
        }
        base.Dispose(disposing);
    }
}
