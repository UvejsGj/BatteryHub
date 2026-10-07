using BatteryHub.Core.Ble;

namespace BatteryHub.Tests.Ble;

internal sealed class FakeBleSource : IBleBatterySource
{
    public List<BleDeviceInfo> Devices { get; } = [];

    /// <summary>Next read results per device ID; the last one repeats.</summary>
    public Dictionary<string, Queue<BleBatteryRead>> Reads { get; } = [];

    public List<string> ReadLog { get; } = [];

    public Exception? ReadError { get; set; }

    public bool Disposed { get; private set; }

    public event EventHandler? DevicesChanged;

    public void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);

    public void SetRead(string id, params BleBatteryRead[] reads) => Reads[id] = new Queue<BleBatteryRead>(reads);

    public Task<IReadOnlyList<BleDeviceInfo>> ListPairedAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BleDeviceInfo>>(Devices.ToList());

    public Task<BleBatteryRead> ReadAsync(BleDeviceInfo device, CancellationToken cancellationToken)
    {
        ReadLog.Add(device.Id);
        if (ReadError is not null)
        {
            throw ReadError;
        }

        var queue = Reads[device.Id];
        return Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
    }

    public void Dispose() => Disposed = true;
}
