using System.Diagnostics;

namespace Testfowm;

internal sealed class PersonTracker
{
    private ResultML? previousTarget;
    private long lastSeenAt;

    public ResultML? Select(IReadOnlyList<ResultML> detections, Size frameSize, long capturedAt, PointF? aim = null)
    {
        // При пропаже цели не продолжаем выдавать её старые координаты.
        if (detections.Count == 0) return null;

        ResultML? selected = null;
        if (previousTarget is not null &&
            Stopwatch.GetElapsedTime(lastSeenAt, capturedAt).TotalMilliseconds < 1000)
        {
            float gate = Math.Max(40, Math.Min(frameSize.Width, frameSize.Height) * 0.15f);
            selected = detections
                .Where(d => ConnectML.IntersectionOverUnion(d.Bounds, previousTarget.Bounds) >= 0.1f ||
                    DistanceSquared(d.Center, previousTarget.Center) <= gate * gate)
                .OrderByDescending(d => ConnectML.IntersectionOverUnion(d.Bounds, previousTarget.Bounds))
                .ThenBy(d => DistanceSquared(d.Center, previousTarget.Center))
                .FirstOrDefault();
            if (selected is null) return null;
        }
        selected ??= detections
            .OrderBy(d => DistanceSquared(d.Center, aim ?? new PointF(frameSize.Width / 2f, frameSize.Height / 2f)))
            .ThenByDescending(d => d.Confidence)
            .First();

        previousTarget = selected;
        lastSeenAt = capturedAt;
        return selected;
    }

    private static float DistanceSquared(PointF first, PointF second) =>
        (first.X - second.X) * (first.X - second.X) +
        (first.Y - second.Y) * (first.Y - second.Y);
}
