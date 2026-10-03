namespace Testfowm;

// Центр 47° и ход по 30° в обе стороны по запросу пользователя.
internal static class MouseXCalibration
{
    public const float Minimum = 17, Home = 47, Maximum = 77, MaximumStep = 2f;

    public static float HorizontalError(RectangleF target, float aimX) =>
        target.Left + target.Width / 2 - aimX;

    // Цель справа: мышь вправо, угол основания уменьшается; слева — увеличивается.
    public static float Destination(float currentBase, float correction, bool invert) =>
        Math.Clamp(currentBase - correction * (invert ? -1 : 1), Minimum, Maximum);

    public static float ReturnHome(float currentBase, float step) =>
        currentBase + Math.Clamp(Home - currentBase, -step, step);
}
