using System.Globalization;

namespace Testfowm;

internal sealed record AuxServoState(int Pin, bool Enabled, bool Attached, bool Moving, float Angle, float Target, float Speed)
{
    internal static bool TryParse(string line, out AuxServoState? state)
    {
        state = null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 8 || parts[0] != "AUXSTATE" || parts[1] != "26" ||
            (parts[2] != "0" && parts[2] != "1") || (parts[3] != "0" && parts[3] != "1") ||
            (parts[4] != "0" && parts[4] != "1")) return false;
        var values = new float[3];
        for (int i = 0; i < values.Length; i++)
            if (!float.TryParse(parts[i + 5], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                !float.IsFinite(values[i])) return false;
        if (values[0] < 0 || values[0] > 180 || values[1] < 0 || values[1] > 180 ||
            values[2] < 15 || values[2] > 180 ||
            (parts[2] == "0" && parts[4] != "0") || (parts[2] == "1" && parts[3] == "0")) return false;
        state = new AuxServoState(26, parts[2] == "1", parts[3] == "1", parts[4] == "1", values[0], values[1], values[2]);
        return true;
    }
}
