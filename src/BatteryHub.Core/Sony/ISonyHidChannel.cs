using HidSharp;

namespace BatteryHub.Core.Sony;

/// <summary>The two HID operations a battery read needs, so the read logic can be tested without hardware.</summary>
internal interface ISonyHidChannel
{
    int MaxInputReportLength { get; }

    int MaxFeatureReportLength { get; }

    /// <summary>Reads the next input report into <paramref name="buffer"/> (report ID at index 0).</summary>
    /// <returns>Bytes read, or 0 if no report arrived within <paramref name="timeout"/>.</returns>
    int ReadInput(byte[] buffer, TimeSpan timeout);

    /// <summary>Gets the feature report whose ID is in <paramref name="buffer"/>[0]. Throws <see cref="IOException"/> if refused.</summary>
    void GetFeature(byte[] buffer);
}

internal sealed class HidSharpChannel(HidDevice device, HidStream stream) : ISonyHidChannel
{
    public int MaxInputReportLength { get; } = device.GetMaxInputReportLength();

    public int MaxFeatureReportLength { get; } = device.GetMaxFeatureReportLength();

    public int ReadInput(byte[] buffer, TimeSpan timeout)
    {
        stream.ReadTimeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
        try
        {
            return stream.Read(buffer, 0, buffer.Length);
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public void GetFeature(byte[] buffer) => stream.GetFeature(buffer);
}
