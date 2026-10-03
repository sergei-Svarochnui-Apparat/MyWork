namespace Testfowm;

// Достаточно немного зайти внутрь рамки, доводка до её центра не требуется.
internal static class TargetAlignment
{
    public const float Inset = 8, StopMargin = 0, ResumeMargin = 4;

    private static RectangleF InnerBounds(RectangleF target)
    {
        float x = Math.Min(Inset, target.Width * .25f), y = Math.Min(Inset, target.Height * .25f);
        return RectangleF.FromLTRB(target.Left + x, target.Top + y, target.Right - x, target.Bottom - y);
    }

    public static PointF NearestPoint(RectangleF target, PointF aim)
    {
        var inner = InnerBounds(target);
        return new PointF(Math.Clamp(aim.X, inner.Left, inner.Right), Math.Clamp(aim.Y, inner.Top, inner.Bottom));
    }

    // Положительный X — цель справа; положительный Y — прицел ниже цели.
    public static float HorizontalError(RectangleF target, float aimX)
    {
        var inner = InnerBounds(target);
        return Math.Clamp(aimX, inner.Left, inner.Right) - aimX;
    }

    public static float VerticalError(RectangleF target, float aimY)
    {
        var inner = InnerBounds(target);
        return aimY - Math.Clamp(aimY, inner.Top, inner.Bottom);
    }

    public static bool IsReached(RectangleF target, PointF aim) =>
        Math.Abs(HorizontalError(target, aim.X)) <= StopMargin &&
        Math.Abs(VerticalError(target, aim.Y)) <= StopMargin;
}
