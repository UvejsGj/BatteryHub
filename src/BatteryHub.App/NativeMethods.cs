using System.Runtime.InteropServices;

namespace BatteryHub.App;

internal static partial class NativeMethods
{
    public const int SmCxSmIcon = 49;
    public const int SmCySmIcon = 50;
    public const int AsfwAny = -1;

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    /// <summary>Lets another process (the running copy) bring a window to the front on this process's behalf.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int processId);
}
