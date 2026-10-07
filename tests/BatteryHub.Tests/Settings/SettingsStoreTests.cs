using BatteryHub.Core.Settings;

namespace BatteryHub.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "batteryhub-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = new SettingsStore(_dir).Load();

        Assert.Equal(new AppSettings(), settings);
        Assert.True(settings.ReadBluetoothPlayStationControllers);
    }

    [Fact]
    public void Saves_camel_case_json_and_loads_it_back()
    {
        var store = new SettingsStore(_dir);

        store.Save(new AppSettings { ReadBluetoothPlayStationControllers = false });

        Assert.Contains("\"readBluetoothPlayStationControllers\": false", File.ReadAllText(store.FilePath));
        Assert.False(store.Load().ReadBluetoothPlayStationControllers);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void Saving_again_replaces_the_file()
    {
        var store = new SettingsStore(_dir);

        store.Save(new AppSettings { ReadBluetoothPlayStationControllers = false });
        store.Save(new AppSettings { ReadBluetoothPlayStationControllers = true });

        Assert.True(store.Load().ReadBluetoothPlayStationControllers);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void Comments_trailing_commas_and_unknown_properties_keep_the_saved_values()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        File.WriteAllText(store.FilePath, """
            {
              // hand edit, or written by a newer version
              "readBluetoothPlayStationControllers": false,
              "someFutureSetting": 42,
            }
            """);

        Assert.False(store.Load().ReadBluetoothPlayStationControllers);
        Assert.True(File.Exists(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".bad"));
    }

    [Fact]
    public void Missing_properties_take_defaults()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        File.WriteAllText(store.FilePath, "{}");

        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void Locked_file_gives_defaults_and_is_not_moved_aside()
    {
        var store = new SettingsStore(_dir);
        store.Save(new AppSettings { ReadBluetoothPlayStationControllers = false });

        // Another process holds the file: reading fails, but renaming it would still be allowed.
        var share = OperatingSystem.IsWindows() ? FileShare.Delete : FileShare.None;
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, share))
        {
            Assert.Equal(new AppSettings(), store.Load());
        }

        Assert.False(File.Exists(store.FilePath + ".bad"));
        Assert.False(store.Load().ReadBluetoothPlayStationControllers);
    }

    [Fact]
    public void Unreadable_file_gives_defaults()
    {
        var store = new SettingsStore(_dir);
        Directory.CreateDirectory(store.FilePath); // reading a directory fails the way a denied file does

        Assert.Equal(new AppSettings(), store.Load());
        Assert.True(Directory.Exists(store.FilePath));
    }

    [Fact]
    public void Corrupt_file_that_cannot_be_moved_aside_still_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        File.WriteAllText(store.FilePath, "{ not json");
        Directory.CreateDirectory(store.FilePath + ".bad");
        File.WriteAllText(Path.Combine(store.FilePath + ".bad", "blocker"), "");

        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_defaults_are_used()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.Equal(new AppSettings(), store.Load());
        Assert.False(File.Exists(store.FilePath));
        Assert.Equal("{ not json", File.ReadAllText(store.FilePath + ".bad"));
    }
}
