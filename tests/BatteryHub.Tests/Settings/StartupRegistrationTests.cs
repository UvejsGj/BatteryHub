using BatteryHub.Core.Windows;

namespace BatteryHub.Tests.Settings;

public class StartupRegistrationTests
{
    private const string Exe = @"C:\Users\me\Apps\BatteryHub\BatteryHub.App.exe";

    private sealed class FakeRegistry : IStartupRegistry
    {
        public Dictionary<string, string> Run { get; } = [];

        public Dictionary<string, byte[]> Approved { get; } = [];

        public string? GetRunCommand(string name) => Run.GetValueOrDefault(name);

        public void SetRunCommand(string name, string command) => Run[name] = command;

        public void DeleteRunCommand(string name) => Run.Remove(name);

        public byte[]? GetApproval(string name) => Approved.GetValueOrDefault(name);

        public void DeleteApproval(string name) => Approved.Remove(name);
    }

    private readonly FakeRegistry _registry = new();

    [Fact]
    public void Enable_writes_the_quoted_path_and_clears_a_task_manager_disable()
    {
        _registry.Approved["BatteryHub"] = [0x03, 0, 0, 0];
        var startup = new StartupRegistration(_registry, Exe);

        startup.Enable();

        Assert.Equal($"\"{Exe}\"", _registry.Run["BatteryHub"]);
        Assert.Empty(_registry.Approved);
        Assert.True(startup.IsEnabled);
    }

    [Fact]
    public void Disable_removes_the_entry()
    {
        var startup = new StartupRegistration(_registry, Exe);
        startup.Enable();

        startup.Disable();

        Assert.Empty(_registry.Run);
        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public void Entry_for_another_copy_of_the_app_counts_as_off()
    {
        _registry.Run["BatteryHub"] = @"""C:\Old\BatteryHub.App.exe""";

        Assert.False(new StartupRegistration(_registry, Exe).IsEnabled);
    }

    [Fact]
    public void Path_comparison_ignores_case_and_accepts_an_unquoted_entry()
    {
        _registry.Run["BatteryHub"] = Exe.ToUpperInvariant();

        Assert.True(new StartupRegistration(_registry, Exe).IsEnabled);
    }

    [Fact]
    public void Entry_disabled_in_task_manager_counts_as_off()
    {
        var startup = new StartupRegistration(_registry, Exe);
        startup.Enable();
        _registry.Approved["BatteryHub"] = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.False(startup.IsEnabled);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(new byte[0], false)]
    [InlineData(new byte[] { 0x02, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x03, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x07, 0, 0, 0 }, true)]
    public void Reads_task_managers_flag(byte[]? approval, bool disabled)
    {
        Assert.Equal(disabled, StartupRegistration.IsDisabledByTaskManager(approval));
    }
}
