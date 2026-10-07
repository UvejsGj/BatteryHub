using Microsoft.Win32;

namespace BatteryHub.Core.Windows;

/// <summary>The registry values behind "start with Windows", abstracted so the logic can be tested.</summary>
public interface IStartupRegistry
{
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\{name}.</summary>
    string? GetRunCommand(string name);

    void SetRunCommand(string name, string command);

    void DeleteRunCommand(string name);

    /// <summary>
    /// HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\{name}: Task Manager's
    /// enabled/disabled flag for the Run entry.
    /// </summary>
    byte[]? GetApproval(string name);

    void DeleteApproval(string name);
}

/// <summary>Starts the app at sign-in through the current user's Run key. Needs no admin rights.</summary>
public sealed class StartupRegistration(IStartupRegistry registry, string executablePath)
{
    public const string ValueName = "BatteryHub";

    /// <summary>The Run command for <paramref name="executablePath"/>: the quoted path.</summary>
    public static string CommandFor(string executablePath) => $"\"{executablePath}\"";

    /// <summary>
    /// True when the Run entry exists, points at this executable, and Task Manager has not disabled it.
    /// An entry pointing at another copy of the app counts as off, so turning it on repoints it here.
    /// </summary>
    public bool IsEnabled =>
        Unquote(registry.GetRunCommand(ValueName)) is { } path
        && string.Equals(Path.GetFullPath(path), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase)
        && !IsDisabledByTaskManager(registry.GetApproval(ValueName));

    public void Enable()
    {
        registry.SetRunCommand(ValueName, CommandFor(executablePath));

        // The user asked for it here, so clear a "disabled" flag Task Manager left behind; no flag means enabled.
        registry.DeleteApproval(ValueName);
    }

    public void Disable()
    {
        registry.DeleteRunCommand(ValueName);
        registry.DeleteApproval(ValueName);
    }

    /// <summary>
    /// Task Manager stores a byte array whose first byte is even when enabled (0x02, 0x06) and odd when disabled
    /// (0x03, 0x07). No value means enabled.
    /// </summary>
    public static bool IsDisabledByTaskManager(byte[]? approval) => approval is { Length: > 0 } && (approval[0] & 1) == 1;

    private static string? Unquote(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        // BatteryHub always writes a quoted path; treat anything else as a bare path.
        return command;
    }
}

/// <summary>The real HKCU registry.</summary>
public sealed class StartupRegistry : IStartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public string? GetRunCommand(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    public void SetRunCommand(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void DeleteRunCommand(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public byte[]? GetApproval(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(name) as byte[];
    }

    public void DeleteApproval(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
