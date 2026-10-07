using BatteryHub.Core;
using Microsoft.Extensions.Time.Testing;

namespace BatteryHub.Tests.Monitor;

public class BatteryMonitorTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly FakeTimeProvider _time = new();

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), $"Timed out waiting for {what}");
    }

    // The poll loop registers its delay asynchronously, so keep nudging the fake clock until the poll happens.
    private async Task AdvanceUntil(Func<bool> condition, string what)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            _time.Advance(Interval);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), $"Timed out waiting for {what}");
    }

    [Fact]
    public async Task Start_polls_every_provider_and_merges_sorted_by_name_then_id()
    {
        // IDs deliberately sort the other way round from names; the two identical pads arrive in reverse ID order;
        // the lower-case name would sort last under an ordinal comparison.
        var pads = new FakeProvider("pads", Interval, [Readings.Of("a", "Zebra pad", 50), Readings.Of("d2", "DualShock 4", 40), Readings.Of("d1", "DualShock 4", 60)]);
        var buds = new FakeProvider("buds", Interval, [Readings.Of("z", "beats Studio", 70)]);
        await using var monitor = new BatteryMonitor([pads, buds], time: _time);

        monitor.Start();
        await WaitFor(() => monitor.Current.Readings.Count == 4, "both providers");

        Assert.Equal(["z", "d1", "d2", "a"], monitor.Current.Readings.Select(r => r.DeviceId));
    }

    [Fact]
    public async Task A_device_the_provider_stops_reporting_disappears()
    {
        var pads = new FakeProvider("pads", Interval, [Readings.Of("p", "Pad", 50)], []);
        await using var monitor = new BatteryMonitor([pads], time: _time);

        monitor.Start();
        await WaitFor(() => monitor.Current.Readings.Count == 1, "first poll");
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Empty(monitor.Current.Readings);

        pads.Push(Readings.Of("p", "Pad", 50));
        Assert.Single(monitor.Current.Readings);
        pads.Push();
        Assert.Empty(monitor.Current.Readings);
    }

    [Fact]
    public async Task Polls_on_the_providers_own_interval_not_sooner()
    {
        var pads = new FakeProvider("pads", Interval, [Readings.Of("p", "Pad", 50)]);
        var buds = new FakeProvider("buds", TimeSpan.FromSeconds(5), [Readings.Of("b", "Buds", 70)]);
        await using var monitor = new BatteryMonitor([pads, buds], time: _time);

        monitor.Start();
        await WaitFor(() => pads.Polls == 1 && buds.Polls == 1, "first polls");
        for (int second = 1; second < 30; second++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, pads.Polls);
        Assert.True(buds.Polls >= 3, $"the 5 s provider should keep polling meanwhile (polled {buds.Polls} times)");
        await AdvanceUntil(() => pads.Polls == 2, "the 30 s poll");
    }

    [Fact]
    public async Task Polls_again_after_the_providers_interval()
    {
        var pads = new FakeProvider("pads", Interval, [Readings.Of("p", "Pad", 50)], [Readings.Of("p", "Pad", 40)]);
        await using var monitor = new BatteryMonitor([pads], time: _time);

        monitor.Start();
        await WaitFor(() => pads.Polls == 1 && monitor.Current.Readings.Count == 1, "first poll");
        await AdvanceUntil(() => monitor.Current.Readings.SingleOrDefault()?.Percent == 40, "second poll");
    }

    [Fact]
    public async Task A_failing_provider_keeps_its_last_readings_and_does_not_stop_others()
    {
        var flaky = new FakeProvider("flaky", Interval, [Readings.Of("f", "Flaky", 30)], null);
        var steady = new FakeProvider("steady", Interval, [Readings.Of("s", "Steady", 90)]);
        await using var monitor = new BatteryMonitor([flaky, steady], time: _time);

        monitor.Start();
        await WaitFor(() => monitor.Current.Readings.Count == 2, "first polls");
        await AdvanceUntil(() => flaky.Polls >= 3 && steady.Polls >= 3, "more polls");

        Assert.Equal(30, monitor.Current.Readings.Single(r => r.DeviceId == "f").Percent);
        Assert.Equal(90, monitor.Current.Readings.Single(r => r.DeviceId == "s").Percent);

        // A manual refresh must not surface the provider's exception either (the UI would crash on it).
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(30, monitor.Current.Readings.Single(r => r.DeviceId == "f").Percent);
    }

    [Fact]
    public async Task Pushed_readings_replace_that_providers_set_and_raise_updated()
    {
        var pads = new FakeProvider("pads", Interval, [Readings.Of("p", "Pad", 50)]);
        await using var monitor = new BatteryMonitor([pads], time: _time);
        var snapshots = new List<BatterySnapshot>();
        monitor.Updated += (_, e) => { lock (snapshots) { snapshots.Add(e.Snapshot); } };

        monitor.Start();
        await WaitFor(() => { lock (snapshots) { return snapshots.Count == 1; } }, "first poll's update");
        pads.Push(Readings.Of("p", "Pad", 45), Readings.Of("q", "Other pad", 80));

        Assert.Equal([45, 80], monitor.Current.Readings.OrderBy(r => r.DeviceId).Select(r => r.Percent));
        lock (snapshots)
        {
            // Updated is raised outside the monitor's lock, so delivery order is not guaranteed; versions are.
            Assert.Equal(snapshots.Count, snapshots.Select(s => s.Version).Distinct().Count());
            var newest = snapshots.MaxBy(s => s.Version)!;
            Assert.Equal([45, 80], newest.Readings.OrderBy(r => r.DeviceId).Select(r => r.Percent));
        }
    }

    [Fact]
    public async Task Same_device_from_two_providers_keeps_the_first_providers_reading()
    {
        var first = new FakeProvider("first", Interval, [Readings.Of("pad", "Pad", 50, "first")]);
        var second = new FakeProvider("second", Interval, [Readings.Of("pad", "Pad", 10, "second")]);
        await using var monitor = new BatteryMonitor([first, second], time: _time);

        monitor.Start();
        await WaitFor(() => first.Polls >= 1 && second.Polls >= 1 && monitor.Current.Readings.Count == 1, "both polls");
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("first", monitor.Current.Readings.Single().Source);
    }

    [Fact]
    public async Task Refresh_waits_for_a_poll_already_running_then_polls_again()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pads = new FakeProvider("pads", Interval, [Readings.Of("p", "Pad", 50)], [Readings.Of("p", "Pad", 49)]) { Hold = hold };
        await using var monitor = new BatteryMonitor([pads], time: _time);

        monitor.Start();
        await WaitFor(() => pads.Polls == 1, "scheduled poll to start");
        var refresh = monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, pads.Polls); // not overlapping the running poll

        hold.SetResult();
        await refresh;

        Assert.Equal(2, pads.Polls);
        Assert.Equal(49, monitor.Current.Readings.Single().Percent);
    }

    [Fact]
    public async Task Dispose_stops_polling_unsubscribes_and_disposes_providers()
    {
        var pads = new FakeProvider("pads", Interval, [Readings.Of("p", "Pad", 50)]);
        var monitor = new BatteryMonitor([pads], time: _time);
        monitor.Start();
        await WaitFor(() => pads.Polls == 1, "first poll");
        Assert.True(pads.HasPushSubscribers);

        await monitor.DisposeAsync();
        int polls = pads.Polls;
        _time.Advance(Interval * 3);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(polls, pads.Polls);
        Assert.False(pads.HasPushSubscribers);
        Assert.True(pads.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => monitor.RefreshAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_provider_returning_null_does_not_freeze_the_others()
    {
        var broken = new FakeProvider("broken", Interval, [null!, Readings.Of("x", "Ok entry", 10)]);
        var steady = new FakeProvider("steady", Interval, [Readings.Of("s", "Steady", 90)], [Readings.Of("s", "Steady", 80)]);
        var nulls = new NullProvider();
        await using var monitor = new BatteryMonitor([broken, nulls, steady], time: _time);

        monitor.Start();
        await WaitFor(() => steady.Polls >= 1 && nulls.Polls >= 1 && broken.Polls >= 1, "first polls");
        nulls.PushNull();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(80, monitor.Current.Readings.Single(r => r.DeviceId == "s").Percent);
        Assert.Equal(10, monitor.Current.Readings.Single(r => r.DeviceId == "x").Percent);
    }

    [Fact]
    public async Task An_interval_beyond_the_maximum_is_clamped_to_it()
    {
        var pads = new FakeProvider("pads", TimeSpan.FromDays(100), [Readings.Of("p", "Pad", 50)]);
        await using var monitor = new BatteryMonitor([pads], time: _time);

        monitor.Start();
        await WaitFor(() => pads.Polls == 1, "first poll");
        for (int i = 0; i < 500 && pads.Polls < 2; i++)
        {
            _time.Advance(BatteryMonitor.MaxPollInterval);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(pads.Polls >= 2, "a 100-day interval would overflow Task.Delay; the clamp keeps polling daily");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_bad_poll_interval_is_clamped_and_disposal_still_works(int seconds)
    {
        var pads = new FakeProvider("pads", TimeSpan.FromSeconds(seconds), [Readings.Of("p", "Pad", 50)]);
        var monitor = new BatteryMonitor([pads], time: _time);

        monitor.Start();
        await WaitFor(() => pads.Polls == 1, "first poll");
        _time.Advance(BatteryMonitor.MinPollInterval / 2);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, pads.Polls); // no busy loop
        for (int i = 0; i < 500 && pads.Polls < 2; i++)
        {
            _time.Advance(BatteryMonitor.MinPollInterval);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(pads.Polls >= 2, "polls continue at the minimum interval");
        await monitor.DisposeAsync();
        Assert.True(pads.Disposed);
    }

    /// <summary>Returns null and pushes null lists: what a buggy provider might do.</summary>
    private sealed class NullProvider : IBatteryProvider
    {
        private int _polls;

        public string Name => "nulls";

        public TimeSpan PollInterval => Interval;

        public int Polls => Volatile.Read(ref _polls);

        public event EventHandler<ReadingsChangedEventArgs>? ReadingsChanged;

        public Task<IReadOnlyList<BatteryReading>> PollAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _polls);
            return Task.FromResult<IReadOnlyList<BatteryReading>>(null!);
        }

        public void PushNull() => ReadingsChanged?.Invoke(this, new ReadingsChangedEventArgs(null!));

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task A_throwing_updated_handler_does_not_break_the_monitor()
    {
        var pads = new FakeProvider("pads", Interval, [Readings.Of("p", "Pad", 50)], [Readings.Of("p", "Pad", 40)]);
        await using var monitor = new BatteryMonitor([pads], time: _time);
        monitor.Updated += (_, _) => throw new InvalidOperationException("handler bug");

        monitor.Start();
        await WaitFor(() => pads.Polls == 1, "first poll");
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(40, monitor.Current.Readings.Single().Percent);

        // A push runs on the provider's thread with no poll-level catch around it.
        pads.Push(Readings.Of("p", "Pad", 35));
        Assert.Equal(35, monitor.Current.Readings.Single().Percent);
    }
}
