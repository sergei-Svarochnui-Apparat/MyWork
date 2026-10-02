using System.Globalization;

namespace Testfowm;

internal sealed record RobotState(bool Armed, bool Moving, float Base, float Shoulder, float Elbow,
    float BaseMin, float BaseMax, float ShoulderMin, float ShoulderMax, float ElbowMin, float ElbowMax)
{
    internal static bool TryParse(string line, out RobotState? state)
    {
        state = null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 12 || parts[0] != "STATE" ||
            (parts[1] != "0" && parts[1] != "1") || (parts[2] != "0" && parts[2] != "1")) return false;
        var values = new float[9];
        for (int i = 0; i < values.Length; i++)
            if (!float.TryParse(parts[i + 3], NumberStyles.Float, CultureInfo.InvariantCulture,
                out values[i]) || !float.IsFinite(values[i])) return false;
        for (int axis = 0; axis < 3; axis++)
            if (values[3 + axis * 2] < 0 || values[4 + axis * 2] > 180 ||
                values[3 + axis * 2] > values[4 + axis * 2] ||
                values[axis] < values[3 + axis * 2] - .01f ||
                values[axis] > values[4 + axis * 2] + .01f) return false;
        state = new RobotState(parts[1] == "1", parts[2] == "1",
            values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]);
        return true;
    }
}
