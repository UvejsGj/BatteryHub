using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

/// <summary>Plays back queued input reports and answers feature requests, advancing a fake clock.</summary>
internal sealed class FakeSonyChannel(FakeTime time) : ISonyHidChannel
{
    private readonly Queue<byte[]> _inputs = new();
    private readonly Dictionary<byte, byte[]> _features = [];

    public int MaxInputReportLength { get; init; } = 64;

    public int MaxFeatureReportLength { get; init; } = 64;

    /// <summary>Simulated time each input report takes to arrive.</summary>
    public TimeSpan ReportInterval { get; init; } = TimeSpan.FromMilliseconds(4);

    /// <summary>When set, reads return <see cref="MaxInputReportLength"/> bytes like Windows does, zero-padded.</summary>
    public bool PadReads { get; init; }

    public Exception? FeatureError { get; init; }

    public List<(byte Id, int Length)> FeatureRequests { get; } = [];

    /// <summary>The timeout passed to each <see cref="ReadInput"/> call.</summary>
    public List<TimeSpan> ReadTimeouts { get; } = [];

    public FakeSonyChannel Input(params byte[][] reports)
    {
        foreach (var report in reports)
        {
            _inputs.Enqueue(report);
        }

        return this;
    }

    public FakeSonyChannel Feature(byte[] report)
    {
        _features[report[0]] = report;
        return this;
    }

    public int ReadInput(byte[] buffer, TimeSpan timeout)
    {
        ReadTimeouts.Add(timeout);
        if (_inputs.Count == 0 || ReportInterval > timeout)
        {
            time.Advance(timeout);
            return 0;
        }

        time.Advance(ReportInterval);
        byte[] report = _inputs.Dequeue();
        Array.Clear(buffer);
        report.CopyTo(buffer, 0);
        return PadReads ? Math.Max(report.Length, MaxInputReportLength) : report.Length;
    }

    public void GetFeature(byte[] buffer)
    {
        FeatureRequests.Add((buffer[0], buffer.Length));
        if (FeatureError is not null)
        {
            throw FeatureError;
        }

        if (_features.TryGetValue(buffer[0], out var report))
        {
            report.AsSpan(0, Math.Min(report.Length, buffer.Length)).CopyTo(buffer);
        }
    }
}

internal sealed class FakeTime : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero).AddTicks(_ticks);

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
