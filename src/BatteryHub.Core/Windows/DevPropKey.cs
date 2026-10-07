using System.Runtime.InteropServices;

namespace BatteryHub.Core.Windows;

/// <summary>DEVPROPKEY: a device property's format ID and property ID (devpropdef.h).</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct DevPropKey(Guid formatId, uint propertyId)
{
    public readonly Guid FormatId = formatId;
    public readonly uint PropertyId = propertyId;

    public override string ToString() => $"{{{FormatId.ToString().ToUpperInvariant()}}} {PropertyId}";
}
