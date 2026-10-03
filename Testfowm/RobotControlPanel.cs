using System.Globalization;
using System.Diagnostics;

namespace Testfowm;

public sealed class RobotControlPanel : UserControl
{
    private readonly ConnectArduino robot = new();
    private readonly System.Windows.Forms.Timer autoTimer = new() { Interval = 20 };
    private readonly Button resumeTracking = new() { Text = "Продолжить наведение", AutoSize = true };
    private readonly SmoothAxisController yController = new(), xController = new();
    private readonly NumericUpDown autoStepX = new()
    {
        Minimum = .1m, Maximum = (decimal)MouseXCalibration.MaximumStep,
        DecimalPlaces = 1, Increment = .1m, Value = 2m, Width = 55
    };
    private readonly NumericUpDown autoStep = new()
    {
        Minimum = .1m, Maximum = 2m, DecimalPlaces = 1, Increment = .1m, Value = 2m, Width = 55
    };
    private readonly NumericUpDown autoPause = new()
    {
        Minimum = 0, Maximum = 500, Increment = 10, Value = 0, Width = 60
    };
    private readonly Label autoInformation = new() { AutoSize = true, Text = "Авто выключено.", MaximumSize = new Size(1000, 0) };
    private sealed record VisionSample(RectangleF? Target, PointF Aim, Size FrameSize, long CapturedAt);
    private VisionSample? vision;
    private sealed record MotionFeedback(VisionSample Before, float StepX, float StepY, long FinishedAt);
    private MotionFeedback? feedback;
    private bool autoEnabled, autoCycleRunning, hasSeenTarget;
    private bool autoRequested = true;
    private long lostSince, lastAutomaticMoveEndedAt;
    public bool AutoTrackingEnabled => autoEnabled;
    public string TrackingAxes => "XY";
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
    private readonly NumericUpDown auxAngle = new() { Minimum = 0, Maximum = 180, DecimalPlaces = 1, Increment = 1, Value = 90, Width = 65 };
    private readonly NumericUpDown auxSpeed = new() { Minimum = 15, Maximum = 180, Increment = 5, Value = 60, Width = 65 };
    private readonly NumericUpDown auxStep = new() { Minimum = .1m, Maximum = 10, DecimalPlaces = 1, Increment = .5m, Value = 2, Width = 55 };
    private readonly Button auxEnable = new() { Text = "Включить D26", AutoSize = true };
    private readonly Button auxSet = new() { Text = "Задать угол", AutoSize = true };
    private readonly Button auxMinus = new() { Text = "D26 −", AutoSize = true };
    private readonly Button auxPlus = new() { Text = "D26 +", AutoSize = true };
    private readonly Button auxStop = new() { Text = "Стоп D26", AutoSize = true };
    private readonly Button auxOff = new() { Text = "Отключить D26", AutoSize = true };
    private readonly Label auxInformation = new() { AutoSize = true };
    private readonly Label auxMessage = new() { AutoSize = true, Text = "Первое включение D26 сразу задаёт выбранный угол. Начальное значение — 90°." };
    private bool auxiliaryBusy;
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
        var autoControls = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(1000, 0) };
        autoControls.Controls.Add(resumeTracking);
        autoControls.Controls.Add(new Label
        {
            Text = $"X: {MouseXCalibration.Minimum}…{MouseXCalibration.Maximum}°, центр {MouseXCalibration.Home}°",
            AutoSize = true, Margin = new Padding(3, 7, 3, 3)
        });
        autoControls.Controls.Add(new Label { Text = "Макс. шаг X, °:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        autoControls.Controls.Add(autoStepX);
        autoControls.Controls.Add(new Label { Text = "Макс. шаг Y, °:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        autoControls.Controls.Add(autoStep);
        autoControls.Controls.Add(new Label { Text = "Пауза, мс:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        autoControls.Controls.Add(autoPause);
        rows.Controls.Add(autoControls);
        rows.Controls.Add(autoInformation);
        rows.Controls.Add(information);
        rows.Controls.Add(message);
        var auxiliaryControls = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(1000, 0) };
        auxiliaryControls.Controls.Add(new Label { Text = "SG90 / D26 — угол, °:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        auxiliaryControls.Controls.Add(auxAngle);
        auxiliaryControls.Controls.Add(new Label { Text = "Скорость, °/с:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        auxiliaryControls.Controls.Add(auxSpeed);
        auxiliaryControls.Controls.Add(auxEnable);
        auxiliaryControls.Controls.Add(auxSet);
        auxiliaryControls.Controls.Add(new Label { Text = "Шаг D26, °:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        auxiliaryControls.Controls.Add(auxStep);
        auxiliaryControls.Controls.Add(auxMinus);
        auxiliaryControls.Controls.Add(auxPlus);
        auxiliaryControls.Controls.Add(auxStop);
        auxiliaryControls.Controls.Add(auxOff);
        rows.Controls.Add(auxiliaryControls);
        rows.Controls.Add(auxInformation);
        rows.Controls.Add(auxMessage);
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
        arm.Click += async (_, _) => { if (await ExecuteAsync("ARM")) RequestAutoTracking(); };
        home.Click += async (_, _) => await ExecuteAsync("HOME");
        stop.Click += async (_, _) => { DisableAuto(); await ExecuteAsync("STOP", true); };
        resumeTracking.Click += (_, _) => RequestAutoTracking();
        autoTimer.Tick += async (_, _) => await AutoTickAsync();
        autoTimer.Start();
        robot.StateChanged += ScheduleUpdate;
        robot.StateReceived += RecordState;
        robot.AuxStateReceived += RecordAuxState;
        auxEnable.Click += async (_, _) => await ExecuteAuxiliaryAsync(AuxMoveCommand("AUXON", (float)auxAngle.Value));
        auxSet.Click += async (_, _) => await ExecuteAuxiliaryAsync(AuxMoveCommand("AUXMOVE", (float)auxAngle.Value));
        auxMinus.Click += async (_, _) => await StepAuxiliaryAsync(-1);
        auxPlus.Click += async (_, _) => await StepAuxiliaryAsync(1);
        auxStop.Click += async (_, _) => await ExecuteAuxiliaryAsync("AUXSTOP", interrupt: true);
        auxOff.Click += async (_, _) => await ExecuteAuxiliaryAsync("AUXOFF", interrupt: true);
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
            await StopAutoTrackingAsync();
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
                RequestAutoTracking();
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

    private async Task<bool> ExecuteAsync(string action, bool interrupt = false, bool automatic = false)
    {
        if (shuttingDown || (!interrupt && busy) || (autoEnabled && !interrupt && !automatic)) return false;
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
            return true;
        }
        catch (Exception ex)
        {
            journal.Command(action, "failed", robot.State, robot.IsConnected, ex.Message);
            message.Text = ex.Message;
            return false;
        }
        finally
        {
            if (!interrupt) busy = false;
            UpdateStatus();
        }
    }

    public void UpdateVision(RectangleF? target, PointF aim, Size frameSize, long capturedAt)
    {
        if (vision is { } previous && (Math.Abs(previous.Aim.Y - aim.Y) > 1 ||
            Math.Abs(previous.Aim.X - aim.X) > 1 || previous.FrameSize != frameSize))
            ResetControllers(forgetResponse: previous.FrameSize != frameSize);
        vision = new VisionSample(target, aim, frameSize, capturedAt);
    }

    private void ResetControllers(bool forgetResponse = false)
    {
        feedback = null;
        yController.Reset(forgetResponse);
        xController.Reset(forgetResponse);
    }

    public void ClearVision() { vision = null; ResetControllers(forgetResponse: true); }

    private void ObserveMotionResponse(VisionSample sample)
    {
        if (feedback is not { } previous || sample.CapturedAt <= previous.FinishedAt) return;
        feedback = null;
        if (sample.Target is not RectangleF target || previous.Before.Target is not RectangleF oldTarget ||
            Stopwatch.GetElapsedTime(previous.Before.CapturedAt, sample.CapturedAt).TotalMilliseconds > 500 ||
            sample.FrameSize != previous.Before.FrameSize ||
            Math.Abs(sample.Aim.X - previous.Before.Aim.X) > 1 || Math.Abs(sample.Aim.Y - previous.Before.Aim.Y) > 1 ||
            target.Width < oldTarget.Width * .75f || target.Width > oldTarget.Width * 1.33f ||
            target.Height < oldTarget.Height * .75f || target.Height > oldTarget.Height * 1.33f) return;
        // Оцениваем отклик при преимущественном движении одной оси, чтобы меньше смешивать X и Y.
        if (Math.Abs(previous.StepY) <= .15f || Math.Abs(previous.StepX) >= Math.Abs(previous.StepY) * 4)
            xController.ObserveResponse(MouseXCalibration.HorizontalError(oldTarget, previous.Before.Aim.X),
                MouseXCalibration.HorizontalError(target, sample.Aim.X), previous.StepX, sample.FrameSize.Width);
        if (Math.Abs(previous.StepX) <= .15f || Math.Abs(previous.StepY) >= Math.Abs(previous.StepX) * 4)
            yController.ObserveResponse(MouseYCalibration.VerticalError(oldTarget, previous.Before.Aim.Y),
                MouseYCalibration.VerticalError(target, sample.Aim.Y), previous.StepY, sample.FrameSize.Height);
    }

    private void DisableAuto(string text = "Авто выключено.")
    {
        autoRequested = false;
        autoEnabled = false;
        autoInformation.Text = text;
        lostSince = 0;
        hasSeenTarget = false;
        ResetControllers();
        UpdateStatus();
    }

    public void RequestAutoTracking()
    {
        if (shuttingDown) return;
        autoRequested = true;
        TryStartAutoTracking();
        UpdateStatus();
    }

    private void TryStartAutoTracking()
    {
        if (!autoRequested || autoEnabled || autoCycleRunning || busy || connecting || shuttingDown) return;
        var state = robot.State;
        if (!robot.IsConnected || robot.StateAgeMs >= 750 || state is not { Armed: true, Moving: false } ||
            vision is null || Stopwatch.GetElapsedTime(vision.CapturedAt).TotalMilliseconds > 400)
        {
            autoInformation.Text = "Автонаведение: ожидание камеры и включённых приводов.";
            return;
        }
        if (!MouseYCalibration.Contains(state))
        {
            autoInformation.Text = "Для авто нужна исходная поза 47° / 110° / 43° либо поза в рабочем диапазоне X/Y.";
            return;
        }
        contactConfirmed.Checked = false;
        autoEnabled = true;
        hasSeenTarget = false;
        ResetControllers();
        lostSince = lastAutomaticMoveEndedAt = 0;
        autoInformation.Text = "Авто: ожидание цели.";
        UpdateStatus();
    }

    public async Task StopAutoTrackingAsync()
    {
        bool wasActive = autoEnabled || autoCycleRunning;
        DisableAuto();
        if (wasActive && robot.IsConnected && !shuttingDown) await ExecuteAsync("STOP", true);
    }

    private async Task AutoTickAsync()
    {
        TryStartAutoTracking();
        if (!autoEnabled || autoCycleRunning || busy || connecting || shuttingDown) return;
        autoCycleRunning = true;
        try
        {
            var state = robot.State;
            if (!robot.IsConnected || robot.StateAgeMs >= 750 || state is not { Armed: true })
            {
                await StopAutoTrackingAsync();
                autoInformation.Text = "Авто остановлено: нет свежей связи с приводами.";
                return;
            }
            if (state.Moving) return;
            if (!MouseYCalibration.Contains(state))
            {
                await StopAutoTrackingAsync();
                autoInformation.Text = "Авто остановлено: текущая поза вне рабочего диапазона X/Y.";
                return;
            }
            long now = Stopwatch.GetTimestamp();
            var sample = vision;
            bool targetVisible = sample?.Target is RectangleF bounds && bounds.Width > 0 && bounds.Height > 0 &&
                Stopwatch.GetElapsedTime(sample.CapturedAt).TotalMilliseconds <= 400;
            float destination = state.Shoulder, baseDestination = state.Base;
            if (!targetVisible)
            {
                ResetControllers();
                if (!hasSeenTarget) { autoInformation.Text = "Авто: ожидание цели."; return; }
                if (lostSince == 0) lostSince = now;
                if (Stopwatch.GetElapsedTime(lostSince, now).TotalMilliseconds < 700)
                { autoInformation.Text = "Авто: цель потеряна, ожидание повторного обнаружения."; return; }
                if (Math.Abs(state.Shoulder - MouseYCalibration.Minimum) < .001f && Math.Abs(state.Elbow - 43) < .001f &&
                    Math.Abs(state.Base - MouseXCalibration.Home) < .001f)
                { autoInformation.Text = "Цель потеряна. Рука в исходной позе, ожидание новой цели."; return; }
                destination = Math.Max(MouseYCalibration.Minimum, state.Shoulder - Math.Min((float)autoStep.Value, .5f));
                baseDestination = MouseXCalibration.ReturnHome(state.Base, Math.Min((float)autoStepX.Value, .5f));
                autoInformation.Text = "Цель потеряна — возврат в исходную позу.";
            }
            else
            {
                hasSeenTarget = true;
                lostSince = 0;
                // Каждая коррекция использует изображение, полученное после предыдущего движения и паузы.
                if (lastAutomaticMoveEndedAt != 0 &&
                    (sample!.CapturedAt <= lastAutomaticMoveEndedAt ||
                    Stopwatch.GetElapsedTime(lastAutomaticMoveEndedAt, sample.CapturedAt).TotalMilliseconds < (double)autoPause.Value)) return;
                ObserveMotionResponse(sample!);
                float errorY = TargetAlignment.VerticalError(sample!.Target!.Value, sample.Aim.Y);
                float errorX = TargetAlignment.HorizontalError(sample.Target.Value, sample.Aim.X);
                var correction = yController.Calculate(errorY, sample.FrameSize.Height, sample.CapturedAt,
                    (float)autoStep.Value);
                var correctionX = xController.Calculate(errorX, sample.FrameSize.Width,
                    sample.CapturedAt, (float)autoStepX.Value);
                if (correction is null && correctionX is null) return;
                if (correction is { Reached: true } && correctionX is { Reached: true })
                { autoInformation.Text = "Авто XY: прицел немного внутри рамки — доводка не нужна."; return; }
                float stepY = correction?.Step ?? 0, stepX = correctionX?.Step ?? 0;
                if (stepY == 0 && stepX == 0)
                {
                    autoInformation.Text = correction is { Reversing: true } || correctionX is { Reversing: true } ?
                        "Авто: подтверждение смены направления по новому кадру." : "Авто: уточнение положения рамки.";
                    return;
                }
                destination = Math.Clamp(state.Shoulder + stepY,
                    MouseYCalibration.Minimum, MouseYCalibration.Maximum);
                baseDestination = MouseXCalibration.Destination(state.Base, stepX, invert: false);
                bool limitX = Math.Abs(stepX) > .001f && Math.Abs(baseDestination - state.Base) < .001f;
                bool limitY = Math.Abs(stepY) > .001f && Math.Abs(destination - state.Shoulder) < .001f;
                autoInformation.Text = $"Авто {TrackingAxes}: до рамки X {errorX:F0} px / Y {errorY:F0} px; " +
                    $"шаг X {Math.Abs(baseDestination - state.Base):F2}° / Y {Math.Abs(destination - state.Shoulder):F2}°." +
                    (limitX ? " Край диапазона X." : "") + (limitY ? " Край диапазона Y." : "");
                // Упор одной оси не мешает другой продолжать наведение.
                if (Math.Abs(destination - state.Shoulder) < .001f && Math.Abs(baseDestination - state.Base) < .001f) return;
            }
            bool completed = await ExecuteAsync(MouseYCalibration.MoveTo(baseDestination, destination), automatic: true);
            lastAutomaticMoveEndedAt = Stopwatch.GetTimestamp();
            if (completed && autoEnabled && targetVisible)
                feedback = new MotionFeedback(sample!, state.Base - baseDestination,
                    destination - state.Shoulder, lastAutomaticMoveEndedAt);
            if (!completed && autoEnabled) DisableAuto("Авто остановлено: " + message.Text);
        }
        catch (Exception ex)
        {
            await StopAutoTrackingAsync();
            autoInformation.Text = "Авто остановлено: " + ex.Message;
        }
        finally { autoCycleRunning = false; }
    }

    private void RecordState(RobotState state) => journal.Observe(state, robot.IsConnected);

    private void RecordAuxState(AuxServoState state) => journal.ObserveAuxiliary(state, robot.IsConnected);

    private string AuxMoveCommand(string command, float angle) => command + " " +
        angle.ToString("0.000", CultureInfo.InvariantCulture) + " " +
        auxSpeed.Value.ToString(CultureInfo.InvariantCulture);

    private async Task StepAuxiliaryAsync(int direction)
    {
        if (robot.AuxiliaryState is not { Enabled: true, Moving: false } state || robot.AuxiliaryStateAgeMs >= 750) return;
        float destination = state.Angle + direction * (float)auxStep.Value;
        if (destination < 0 || destination > 180) { auxMessage.Text = "D26: достигнут программный край 0…180°."; return; }
        auxAngle.Value = (decimal)destination;
        await ExecuteAuxiliaryAsync(AuxMoveCommand("AUXMOVE", destination));
    }

    private async Task ExecuteAuxiliaryAsync(string action, bool interrupt = false)
    {
        if (shuttingDown || connecting || !robot.IsConnected || robot.AuxiliaryStateAgeMs >= 750 ||
            !interrupt && auxiliaryBusy) return;
        if (!interrupt) auxiliaryBusy = true;
        UpdateStatus();
        try
        {
            journal.AuxiliaryCommand(action, "sent", robot.AuxiliaryState, robot.IsConnected);
            await robot.SendAsync(action, TimeSpan.FromSeconds(20));
            journal.AuxiliaryCommand(action, "done", robot.AuxiliaryState, robot.IsConnected);
            auxMessage.Text = action == "AUXOFF" ? "D26 отключён, удержание снято." :
                action == "AUXSTOP" ? "D26 остановлен, угол удерживается." : "D26: выполнено " + action;
        }
        catch (Exception ex)
        {
            journal.AuxiliaryCommand(action, "failed", robot.AuxiliaryState, robot.IsConnected, ex.Message);
            auxMessage.Text = "D26: " + ex.Message;
        }
        finally { if (!interrupt) auxiliaryBusy = false; UpdateStatus(); }
    }

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
        !autoEnabled && !busy && !connecting && !shuttingDown && !savingPoint;

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
        arm.Enabled = linked && fresh && state is { Armed: false } && !autoEnabled && !busy && !shuttingDown;
        stop.Enabled = linked && !shuttingDown;
        home.Enabled = linked && fresh && state is { Armed: true, Moving: false } && !autoEnabled && !busy && !shuttingDown;
        resumeTracking.Enabled = !shuttingDown && !connecting && !autoEnabled && !autoRequested;
        autoStepX.Enabled = autoStep.Enabled = autoPause.Enabled = !shuttingDown;
        var auxiliary = robot.AuxiliaryState;
        bool auxReady = linked && robot.AuxiliaryStateAgeMs < 750 && auxiliary is not null && !connecting && !shuttingDown;
        auxEnable.Enabled = auxReady && auxiliary is { Enabled: false } && !auxiliaryBusy;
        auxSet.Enabled = auxMinus.Enabled = auxPlus.Enabled = auxReady && auxiliary is { Enabled: true, Moving: false } && !auxiliaryBusy;
        auxStop.Enabled = auxReady && auxiliary is { Enabled: true };
        auxOff.Enabled = auxReady;
        auxAngle.Enabled = auxSpeed.Enabled = auxStep.Enabled = !auxiliaryBusy && !shuttingDown;
        auxInformation.Text = auxiliary is null ? "D26: " + (linked ? "для управления загрузи новую прошивку RobotBridge." : "нет связи.") :
            $"D26 — командный угол {auxiliary.Angle:F1}°, цель {auxiliary.Target:F1}°, скорость {auxiliary.Speed:F0}°/с | " +
            (!auxReady ? "телеметрия устарела" : !auxiliary.Attached ? "отключён, удержание снято" :
                !auxiliary.Enabled ? "движение заблокировано, угол удерживается" : auxiliary.Moving ? "движется" : "удерживает угол");
        foreach (var button in motionButtons) button.Enabled = home.Enabled;
        savePoint.Enabled = CanSavePoint(state);
        contactConfirmed.Enabled = linked && fresh && state is { Armed: true, Moving: false } && !autoEnabled && !busy && !shuttingDown;
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
        DisableAuto();
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
            autoTimer.Stop();
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
            robot.AuxStateReceived -= RecordAuxState;
            statusTimer.Dispose();
            autoTimer.Dispose();
            robot.Dispose();
            journal.Dispose();
        }
        base.Dispose(disposing);
    }
}
