using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryHub.Core;

/// <summary>Every device's latest reading, in display order.</summary>
/// <param name="Version">Increases with every snapshot; a consumer can drop one older than the last it showed.</param>
public sealed record BatterySnapshot(long Version, IReadOnlyList<BatteryReading> Readings);

public sealed class BatterySnapshotEventArgs(BatterySnapshot snapshot) : EventArgs
{
    public BatterySnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// Owns the providers: polls each on its own interval, listens for their pushed changes, merges their readings and
/// raises <see cref="Updated"/>. A provider that throws is logged and keeps its last good readings; it never stops the
/// monitor or the other providers.
/// </summary>
public sealed class BatteryMonitor : IAsyncDisposable
{
    /// <summary>Poll intervals outside this range are clamped to it, so a bad value cannot spin or stop a provider.</summary>
    public static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxPollInterval = TimeSpan.FromHours(24);

    private readonly ProviderState[] _providers;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly List<Task> _loops = [];
    private long _version;
    private BatterySnapshot _current = new(0, []);
    private bool _started;
    private bool _disposed;

    public BatteryMonitor(IEnumerable<IBatteryProvider> providers, ILogger<BatteryMonitor>? logger = null, TimeProvider? time = null)
    {
        _providers = providers.Select(p => new ProviderState(p)).ToArray();
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised on a thread-pool thread after any provider's readings change.</summary>
    public event EventHandler<BatterySnapshotEventArgs>? Updated;

    public BatterySnapshot Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <summary>Polls every provider now, then on each provider's own interval.</summary>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
            foreach (var state in _providers)
            {
                state.Interval = ValidInterval(state.Provider);
                state.Provider.ReadingsChanged += state.OnPushed = (_, e) => Store(state, e?.Readings);
                _loops.Add(Task.Run(() => RunAsync(state, _stop.Token)));
            }
        }
    }

    /// <summary>Polls every provider now and completes once all have answered or failed.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.WhenAll(_providers.Select(p => PollAsync(p, cancellationToken)));
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        foreach (var state in _providers)
        {
            if (state.OnPushed is { } handler)
            {
                state.Provider.ReadingsChanged -= handler;
            }
        }

        try
        {
            await Task.WhenAll(_loops).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Loops catch everything themselves; this is a last guard so the providers below still get disposed.
            _logger.LogError(ex, "A provider loop ended with an error");
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var state in _providers)
        {
            try
            {
                state.Provider.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Disposing provider {Provider} failed", state.Provider.Name);
            }
        }

        _stop.Dispose();
    }

    private async Task RunAsync(ProviderState state, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await PollAsync(state, stop).ConfigureAwait(false);
            try
            {
                await Task.Delay(state.Interval, _time, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private TimeSpan ValidInterval(IBatteryProvider provider)
    {
        TimeSpan interval;
        try
        {
            interval = provider.PollInterval;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Provider} has no readable poll interval; using {Interval}", provider.Name, MaxPollInterval);
            return MaxPollInterval;
        }

        var clamped = interval < MinPollInterval ? MinPollInterval : interval > MaxPollInterval ? MaxPollInterval : interval;
        if (clamped != interval)
        {
            _logger.LogWarning("Provider {Provider} asked for a {Interval} poll interval; using {Clamped}", provider.Name, interval, clamped);
        }

        return clamped;
    }

    private async Task PollAsync(ProviderState state, CancellationToken cancellationToken)
    {
        // One poll per provider at a time: a manual refresh waits for a scheduled poll already running.
        try
        {
            await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var readings = await state.Provider.PollAsync(cancellationToken).ConfigureAwait(false);
            Store(state, readings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Provider} failed; keeping its last readings", state.Provider.Name);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    // A null list or null entries from a misbehaving provider are dropped here, so they can never reach Merge and
    // freeze every other provider's updates.
    private void Store(ProviderState state, IReadOnlyList<BatteryReading>? readings)
    {
        var clean = readings?.Where(r => r is not null).ToArray() ?? [];
        BatterySnapshot snapshot;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            state.Readings = clean;
            snapshot = _current = new BatterySnapshot(++_version, Merge());
        }

        try
        {
            Updated?.Invoke(this, new BatterySnapshotEventArgs(snapshot));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An Updated handler threw");
        }
    }

    private List<BatteryReading> Merge() => Combine(_providers.Select(p => p.Readings));

    /// <summary>One reading per device, sorted by name. Earlier providers win ties.</summary>
    internal static List<BatteryReading> Combine(IEnumerable<IReadOnlyList<BatteryReading>> readingsByProvider)
    {
        var merged = ReadingDeduplicator.Merge(readingsByProvider);
        merged.Sort((a, b) =>
        {
            int byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.DeviceId, b.DeviceId);
        });
        return merged;
    }

    private sealed class ProviderState(IBatteryProvider provider)
    {
        public IBatteryProvider Provider { get; } = provider;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public IReadOnlyList<BatteryReading> Readings { get; set; } = [];

        public TimeSpan Interval { get; set; }

        public EventHandler<ReadingsChangedEventArgs>? OnPushed { get; set; }
    }
}
