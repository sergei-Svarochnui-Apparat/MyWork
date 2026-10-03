using AForge.Video;
using AForge.Video.DirectShow;
using System.Diagnostics;

namespace Testfowm;

internal sealed class ConnectCamera : IDisposable
{
    private readonly object frameLock = new();
    private VideoCaptureDevice? videoSource;
    private Bitmap? currentBitmap;
    private long sequence;
    private long capturedAt;
    private string? error;

    public static FilterInfo[] GetCameras() =>
        new FilterInfoCollection(FilterCategory.VideoInputDevice).Cast<FilterInfo>().ToArray();

    public string? Error
    {
        get { lock (frameLock) return error; }
    }

    public void StartCamera(string moniker)
    {
        if (videoSource is not null)
            throw new InvalidOperationException("Камера уже запущена.");

        var source = new VideoCaptureDevice(moniker);
        var capabilities = source.VideoCapabilities;
        var preferred = capabilities
            .Where(c => c.FrameSize.Width == 800 && c.FrameSize.Height == 600)
            .OrderByDescending(c => c.AverageFrameRate)
            .FirstOrDefault() ?? capabilities
            .Where(c => c.FrameSize.Width <= 800 && c.FrameSize.Height <= 600 &&
                c.FrameSize.Width >= 320 && c.FrameSize.Height >= 240)
            .OrderByDescending(c => c.AverageFrameRate >= 30)
            .ThenByDescending(c => c.FrameSize.Width * c.FrameSize.Height)
            .ThenByDescending(c => c.AverageFrameRate)
            .FirstOrDefault() ?? capabilities
                .OrderByDescending(c => c.AverageFrameRate)
                .ThenBy(c => c.FrameSize.Width * c.FrameSize.Height)
                .FirstOrDefault();
        if (preferred is not null)
            source.VideoResolution = preferred;

        lock (frameLock)
        {
            error = null;
            sequence = 0;
        }
        source.NewFrame += NewFrameHandler;
        source.VideoSourceError += VideoErrorHandler;
        videoSource = source;
        source.Start();
    }

    private void NewFrameHandler(object sender, NewFrameEventArgs e)
    {
        long acquiredAt = Stopwatch.GetTimestamp();
        var nextFrame = (Bitmap)e.Frame.Clone();
        lock (frameLock)
        {
            currentBitmap?.Dispose();
            currentBitmap = nextFrame;
            capturedAt = acquiredAt;
            sequence++;
        }
    }

    private void VideoErrorHandler(object sender, VideoSourceErrorEventArgs e)
    {
        lock (frameLock) error = e.Description;
    }

    // Каждый читатель получает собственный кадр; очередь старых кадров отсутствует.
    public Bitmap? GetBitmap(long previousSequence, out long frameSequence, out long frameCapturedAt)
    {
        lock (frameLock)
        {
            frameSequence = sequence;
            frameCapturedAt = capturedAt;
            return currentBitmap is null || sequence == previousSequence ? null : (Bitmap)currentBitmap.Clone();
        }
    }

    public void StopCamera()
    {
        var source = videoSource;
        if (source is not null)
        {
            if (source.IsRunning)
            {
                source.SignalToStop();
                source.WaitForStop();
            }
            source.NewFrame -= NewFrameHandler;
            source.VideoSourceError -= VideoErrorHandler;
            videoSource = null;
        }
        lock (frameLock)
        {
            currentBitmap?.Dispose();
            currentBitmap = null;
            sequence = 0;
        }
    }

    public void Dispose() => StopCamera();
}
