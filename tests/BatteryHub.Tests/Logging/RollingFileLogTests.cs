using BatteryHub.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace BatteryHub.Tests.Logging;

public sealed class RollingFileLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "batteryhub-log-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private RollingFileLog Log(long maxBytes = 1024 * 1024, int keepFiles = 3) =>
        new(_directory, maxBytes, keepFiles, _time) { MinimumLevel = LogLevel.Information };

    private string Read(string name = RollingFileLog.FileName) => File.ReadAllText(Path.Combine(_directory, name));

    [Fact]
    public void Writes_time_level_category_message_and_exception()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        using (var log = Log())
        {
            log.CreateLogger<RollingFileLogTests>().LogWarning(new InvalidOperationException("boom"), "Reading {Name} failed", "DualShock 4");
        }

        string text = Read();
        Assert.StartsWith("2026-10-20 09:30:00.000 WRN RollingFileLogTests: Reading DualShock 4 failed", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void Skips_levels_below_the_minimum()
    {
        using (var log = Log())
        {
            var logger = log.CreateLogger<RollingFileLogTests>();
            logger.LogDebug("hidden");
            logger.LogInformation("shown");
            Assert.False(logger.IsEnabled(LogLevel.Debug));
        }

        Assert.DoesNotContain("hidden", Read());
        Assert.Contains("shown", Read());
    }

    [Fact]
    public void Appends_across_runs()
    {
        using (var first = Log())
        {
            first.Write(LogLevel.Information, "App", "first run", null);
        }

        using (var second = Log())
        {
            second.Write(LogLevel.Information, "App", "second run", null);
        }

        Assert.Equal(2, Read().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Rolls_over_and_keeps_only_the_newest_files()
    {
        using (var log = Log(maxBytes: 1024, keepFiles: 2))
        {
            for (int i = 0; i < 40; i++)
            {
                log.Write(LogLevel.Information, "App", $"line {i:D2} " + new string('x', 80), null);
            }
        }

        var files = Directory.GetFiles(_directory).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["batteryhub.1.log", "batteryhub.2.log", "batteryhub.log"], files);
        Assert.All(files, f => Assert.InRange(new FileInfo(Path.Combine(_directory, f!)).Length, 1, 1024));
        Assert.Contains("line 39", Read());
        Assert.DoesNotContain("line 00 ", string.Concat(files.Select(f => Read(f!)))); // the oldest lines are gone
    }

    [Fact]
    public void Keeping_no_old_files_starts_afresh()
    {
        using (var log = Log(maxBytes: 1024, keepFiles: 0))
        {
            for (int i = 0; i < 20; i++)
            {
                log.Write(LogLevel.Information, "App", new string('x', 100), null);
            }
        }

        Assert.Equal([RollingFileLog.FileName], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void An_unwritable_folder_never_throws()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "a file where the folder should be");
        try
        {
            using var log = new RollingFileLog(_directory, time: _time);
            log.Write(LogLevel.Error, "App", "dropped", null);
            log.Write(LogLevel.Error, "App", "dropped too", null);
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    [Fact]
    public void Writes_after_dispose_are_dropped()
    {
        var log = Log();
        log.Dispose();

        log.Write(LogLevel.Error, "App", "late", null);

        Assert.False(File.Exists(Path.Combine(_directory, RollingFileLog.FileName)));
    }
}
