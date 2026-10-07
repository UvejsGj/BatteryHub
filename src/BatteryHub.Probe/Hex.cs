namespace BatteryHub.Probe;

internal static class Hex
{
    /// <summary>16 space-separated bytes per line.</summary>
    public static IEnumerable<string> Lines(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i += 16)
        {
            yield return string.Join(" ", bytes.Skip(i).Take(16).Select(b => b.ToString("X2")));
        }
    }

    /// <summary>One line, trailing zeros dropped (Windows pads reads to the longest report).</summary>
    public static string Trimmed(byte[] bytes)
    {
        int length = bytes.Length;
        while (length > 1 && bytes[length - 1] == 0)
        {
            length--;
        }

        string hex = string.Join(" ", bytes.Take(length).Select(b => b.ToString("X2")));
        return length < bytes.Length ? $"{hex} (+{bytes.Length - length} zero bytes)" : hex;
    }
}
