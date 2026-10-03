using System.Globalization;

namespace Testfowm;

// Пары углов из двух записанных проходов. Расчёт выполняется только на ПК.
internal static class MouseYCalibration
{
    public const float Minimum = 110, Maximum = 135;
    private static readonly (float Shoulder, float Elbow)[] Points =
    {
        (110, 43), (111, 44), (113, 46), (114, 47.5f), (114.5f, 48),
        (119, 51.5f), (121, 53.5f), (122.5f, 55.5f), (123.5f, 56),
        (125, 57.5f), (125.5f, 57.5f), (127.5f, 59.5f), (131, 63), (133, 65), (135, 67)
    };

    public static float ElbowAt(float shoulder)
    {
        if (!float.IsFinite(shoulder) || shoulder < Minimum || shoulder > Maximum)
            throw new ArgumentOutOfRangeException(nameof(shoulder));
        for (int i = 1; i < Points.Length; i++)
        {
            var left = Points[i - 1];
            var right = Points[i];
            if (shoulder <= right.Shoulder)
                return left.Elbow + (right.Elbow - left.Elbow) *
                    (shoulder - left.Shoulder) / (right.Shoulder - left.Shoulder);
        }
        return Points[^1].Elbow;
    }

    public static bool Contains(RobotState state) => state.Shoulder >= Minimum && state.Shoulder <= Maximum &&
        state.Base >= MouseXCalibration.Minimum && state.Base <= MouseXCalibration.Maximum &&
        Math.Abs(state.Elbow - ElbowAt(state.Shoulder)) <= .02f;

    public static string MoveTo(float baseAngle, float shoulder)
    {
        if (!float.IsFinite(baseAngle) || baseAngle < MouseXCalibration.Minimum || baseAngle > MouseXCalibration.Maximum)
            throw new ArgumentOutOfRangeException(nameof(baseAngle));
        return "MOVE " + baseAngle.ToString("0.000", CultureInfo.InvariantCulture) + " " +
            shoulder.ToString("0.000", CultureInfo.InvariantCulture) + " " +
            ElbowAt(shoulder).ToString("0.000", CultureInfo.InvariantCulture);
    }

    // Положительная ошибка: прицел ниже центра цели, требуется поднять изображение.
    public static float VerticalError(RectangleF target, float aimY) =>
        aimY - (target.Top + target.Height / 2);
}
