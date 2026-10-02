namespace Testfowm;

internal sealed record ResultML(RectangleF Bounds, float Confidence)
{
    public PointF Center => new(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);
}
