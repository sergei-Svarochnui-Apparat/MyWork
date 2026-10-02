using System.Diagnostics;

namespace Testfowm;

public partial class Form1 : Form
{
    private const double MaximumDetectionAgeMs = 750;
    private readonly ConnectCamera camera = new();
    private ConnectML? detector;
    private CancellationTokenSource? cancellation;
    private Task? detectionTask;
    private volatile DetectionSnapshot? snapshot;
    private volatile string? detectionError;
    private bool transition;
    private bool allowClose;
    private bool closing;
    private bool closingRobot;
    private long displayedSequence;
    private int previewFrames;
    private long fpsStartedAt = Stopwatch.GetTimestamp();
    private double previewFps;
    private volatile float confidenceThreshold = .40f;
    private sealed record AimPosition(float X, float Y);
    private volatile AimPosition aimPosition = new(.5f, .5f);

    private sealed record CameraChoice(string Name, string Moniker)
    {
        public override string ToString() => Name;
    }

    private sealed record DetectionSnapshot(ResultML[] People, ResultML? Target,
        Size FrameSize, long CapturedAt, double InferenceMs);

    public Form1()
    {
        InitializeComponent();
        RefreshCameras();
    }

    private void RefreshCameras_Click(object? sender, EventArgs e) => RefreshCameras();

    private void Confidence_ValueChanged(object? sender, EventArgs e) =>
        confidenceThreshold = (float)confidenceInput.Value / 100;

    private void CenterButton_Click(object? sender, EventArgs e) => aimPosition = new AimPosition(.5f, .5f);

    private void Preview_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || myPictureBox.Image is not Image image) return;
        var point = MapPreviewPoint(e.Location, myPictureBox.ClientSize, image.Size);
        if (point is PointF position) aimPosition = new AimPosition(position.X, position.Y);
    }

    internal static PointF? MapPreviewPoint(Point point, Size previewSize, Size imageSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0 ||
            previewSize.Width <= 0 || previewSize.Height <= 0) return null;
        float scale = Math.Min(previewSize.Width / (float)imageSize.Width,
            previewSize.Height / (float)imageSize.Height);
        float width = imageSize.Width * scale;
        float height = imageSize.Height * scale;
        float left = (previewSize.Width - width) / 2f;
        float top = (previewSize.Height - height) / 2f;
        if (point.X < left || point.Y < top || point.X > left + width || point.Y > top + height)
            return null;
        return new PointF((point.X - left) / width, (point.Y - top) / height);
    }

    private void RefreshCameras()
    {
        try
        {
            cameraSelector.Items.Clear();
            foreach (var device in ConnectCamera.GetCameras())
                cameraSelector.Items.Add(new CameraChoice(device.Name, device.MonikerString));
            if (cameraSelector.Items.Count > 0) cameraSelector.SelectedIndex = 0;
            else statusLabel.Text = "Камера не найдена. Проверь подключение и нажми «Обновить камеры».";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Не удалось получить список камер: " + ex.Message;
        }
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        if (transition) return;
        transition = true;
        startButton.Enabled = false;
        try
        {
            if (cancellation is null) await StartCaptureAsync();
            else await StopCaptureAsync();
        }
        finally
        {
            transition = false;
            startButton.Enabled = !closing;
            if (closing) FinishClosing();
        }
    }

    private async Task StartCaptureAsync()
    {
        if (cameraSelector.SelectedItem is not CameraChoice choice)
        {
            statusLabel.Text = "Сначала выбери доступную камеру.";
            return;
        }
        cameraSelector.Enabled = refreshButton.Enabled = false;
        statusLabel.Text = "Загрузка модели…";
        try
        {
            string modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "yolov8n.onnx");
            detector = await Task.Run(() => new ConnectML(modelPath));
            if (closing)
            {
                detector.Dispose();
                detector = null;
                return;
            }
            camera.StartCamera(choice.Moniker);
            cancellation = new CancellationTokenSource();
            snapshot = null;
            detectionError = null;
            displayedSequence = 0;
            previewFrames = 0;
            previewFps = 0;
            fpsStartedAt = Stopwatch.GetTimestamp();
            var localDetector = detector;
            var token = cancellation.Token;
            detectionTask = Task.Run(() => DetectionLoopAsync(localDetector, token));
            previewTimer.Start();
            startButton.Text = "Остановить камеру";
            statusLabel.Text = "Ожидание первого кадра…";
        }
        catch (Exception ex)
        {
            await cameraStopAsync();
            detector?.Dispose();
            detector = null;
            cameraSelector.Enabled = refreshButton.Enabled = true;
            statusLabel.Text = "Не удалось запустить: " + ex.Message;
        }
    }

    private async Task DetectionLoopAsync(ConnectML model, CancellationToken token)
    {
        long lastSequence = -1;
        var tracker = new PersonTracker();
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var frame = camera.GetBitmap(out long sequence, out long capturedAt);
                if (frame is null || sequence == lastSequence)
                {
                    await Task.Delay(10, token);
                    continue;
                }
                lastSequence = sequence;
                long startedAt = Stopwatch.GetTimestamp();
                var people = model.Detect(frame, confidenceThreshold);
                if (token.IsCancellationRequested) break;
                var currentAim = aimPosition;
                var aimSquare = GetCenterSquare(frame.Size, new PointF(currentAim.X, currentAim.Y));
                var target = tracker.Select(people, frame.Size, capturedAt,
                    new PointF(aimSquare.Left + aimSquare.Width / 2, aimSquare.Top + aimSquare.Height / 2));
                snapshot = new DetectionSnapshot(people.ToArray(), target, frame.Size, capturedAt,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            snapshot = null;
            detectionError = ex.Message;
        }
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        var frame = camera.GetBitmap(out long sequence, out long rawCapturedAt);
        if (frame is null || sequence == displayedSequence)
        {
            frame?.Dispose();
            if (camera.Error is string error) statusLabel.Text = "Ошибка камеры: " + error;
            else if (rawCapturedAt != 0 &&
                Stopwatch.GetElapsedTime(rawCapturedAt).TotalMilliseconds > 1000)
            {
                statusLabel.Text = "Новые кадры камеры не поступают.";
                targetLabel.Text = "Координаты цели устарели. Автослежение выключено.";
            }
            return;
        }
        try
        {
            displayedSequence = sequence;
            previewFrames++;
            double seconds = Stopwatch.GetElapsedTime(fpsStartedAt).TotalSeconds;
            if (seconds >= 1)
            {
                previewFps = previewFrames / seconds;
                previewFrames = 0;
                fpsStartedAt = Stopwatch.GetTimestamp();
            }

            var current = snapshot;
            double detectionAge = current is null ? 0 :
                Stopwatch.GetElapsedTime(current.CapturedAt).TotalMilliseconds;
            bool fresh = current is not null && current.FrameSize == frame.Size &&
                detectionAge <= MaximumDetectionAgeMs;
            var currentAim = aimPosition;
            var greenSquare = GetCenterSquare(frame.Size, new PointF(currentAim.X, currentAim.Y));
            var aim = new PointF(greenSquare.Left + greenSquare.Width / 2,
                greenSquare.Top + greenSquare.Height / 2);

            using (var graphics = Graphics.FromImage(frame))
            using (var pen = new Pen(Color.Lime, 3))
            {
                graphics.DrawRectangle(pen, greenSquare.X, greenSquare.Y,
                    greenSquare.Width, greenSquare.Height);
                if (fresh && current is not null)
                {
                    using var targetPen = new Pen(Color.Red, 3);
                    using var otherPen = new Pen(Color.Goldenrod, 1);
                    foreach (var person in current.People)
                    {
                        var bounds = person.Bounds;
                        graphics.DrawRectangle(person == current.Target ? targetPen : otherPen,
                            bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    }
                    if (current.Target is ResultML target)
                    {
                        graphics.DrawLine(Pens.Cyan, aim.X, aim.Y,
                            target.Center.X, target.Center.Y);
                        bool aligned = target.Bounds.Contains(greenSquare);
                        float dx = target.Center.X - aim.X;
                        float dy = target.Center.Y - aim.Y;
                        targetLabel.Text = $"Цель: {target.Confidence:P0} | ΔX: {dx:F0} px, ΔY: {dy:F0} px | " +
                            $"ошибка: {dx / (frame.Width / 2f):P0}, {dy / (frame.Height / 2f):P0} | " +
                            (aligned ? "Зелёный квадрат внутри цели" : "Цель вне прицела") +
                            " | Автослежение выключено";
                    }
                    else targetLabel.Text = "Цель потеряна — ожидание человека. Автослежение выключено.";
                }
                else
                {
                    targetLabel.Text = current is null ? "Поиск человека… Автослежение выключено." :
                        "Координаты цели устарели. Автослежение выключено.";
                }
            }

            statusLabel.Text = detectionError is not null ?
                "Ошибка распознавания: " + detectionError :
                $"Камера: {frame.Width}×{frame.Height} | показ: {previewFps:F1} FPS | " +
                (current is null ? "Распознавание: ожидание" :
                $"YOLO: {current.InferenceMs:F0} ms | возраст обработанного кадра: {detectionAge:F0} ms");

            var oldImage = myPictureBox.Image;
            myPictureBox.Image = frame;
            frame = null; // PictureBox теперь владеет этим Bitmap.
            oldImage?.Dispose();
        }
        finally { frame?.Dispose(); }
    }

    internal static RectangleF GetCenterSquare(Size frameSize, PointF normalizedCenter)
    {
        float side = Math.Min(75, Math.Min(frameSize.Width, frameSize.Height));
        float centerX = Math.Clamp(normalizedCenter.X * frameSize.Width, side / 2, frameSize.Width - side / 2);
        float centerY = Math.Clamp(normalizedCenter.Y * frameSize.Height, side / 2, frameSize.Height - side / 2);
        return new RectangleF(centerX - side / 2, centerY - side / 2, side, side);
    }

    private Task cameraStopAsync() => Task.Run(camera.StopCamera);

    private async Task StopCaptureAsync()
    {
        previewTimer.Stop();
        statusLabel.Text = "Остановка камеры…";
        cancellation?.Cancel();
        if (detectionTask is not null) await detectionTask;
        await cameraStopAsync();
        detector?.Dispose();
        detector = null;
        cancellation?.Dispose();
        cancellation = null;
        detectionTask = null;
        snapshot = null;
        detectionError = null;
        var oldImage = myPictureBox.Image;
        myPictureBox.Image = null;
        oldImage?.Dispose();
        startButton.Text = "Запустить камеру";
        cameraSelector.Enabled = refreshButton.Enabled = true;
        statusLabel.Text = "Камера остановлена.";
        targetLabel.Text = "Режим наблюдения. Автослежение выключено.";
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (allowClose)
        {
            base.OnFormClosing(e);
            return;
        }
        e.Cancel = true;
        base.OnFormClosing(e);
        if (closing) return;
        closing = true;
        startButton.Enabled = false;
        if (transition) return; // Завершение уже выполняющегося запуска/остановки закроет окно.
        await StopCaptureAsync();
        FinishClosing();
    }

    private async void FinishClosing()
    {
        if (closingRobot) return;
        closingRobot = true;
        try { await robotPanel.ShutdownAsync(); }
        finally
        {
            allowClose = true;
            Close();
        }
    }
}
