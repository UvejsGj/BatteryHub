using System.Runtime.InteropServices;
using System.Text;

namespace BatteryHub.Core.Windows;

/// <summary>One device property as CfgMgr32 returned it.</summary>
/// <param name="Result">CONFIGRET; 0 (CR_SUCCESS) when <paramref name="Data"/> holds the value.</param>
/// <param name="Type">DEVPROPTYPE (devpropdef.h).</param>
internal readonly record struct DeviceProperty(uint Result, uint Type, byte[] Data)
{
    // devpropdef.h
    public const uint TypeByte = 0x03;
    public const uint TypeUInt32 = 0x07;
    public const uint TypeGuid = 0x0D;
    public const uint TypeBoolean = 0x11;
    public const uint TypeString = 0x12;
    public const uint TypeStringList = 0x2012; // DEVPROP_TYPEMOD_LIST | DEVPROP_TYPE_STRING

    public bool Found => Result == DeviceNodes.CrSuccess;

    public string? AsString() =>
        Found && Type == TypeString ? Encoding.Unicode.GetString(Data).TrimEnd('\0') : null;

    public Guid? AsGuid() =>
        Found && Type == TypeGuid && Data.Length == 16 ? new Guid(Data) : null;

    /// <summary>Type and value in words, for the Probe.</summary>
    public string Describe()
    {
        if (!Found)
        {
            return Result == DeviceNodes.CrNoSuchValue ? "not set" : $"CONFIGRET 0x{Result:X2}";
        }

        string hex = Data.Length == 0 ? "no data" : Convert.ToHexString(Data);
        return Type switch
        {
            TypeByte when Data.Length == 1 => $"BYTE 0x{Data[0]:X2} ({Data[0]})",
            TypeUInt32 when Data.Length == 4 => $"UINT32 0x{BitConverter.ToUInt32(Data):X8}",
            TypeBoolean when Data.Length == 1 => $"BOOLEAN {(Data[0] != 0 ? "true" : "false")} (0x{Data[0]:X2})",
            TypeGuid when Data.Length == 16 => $"GUID {new Guid(Data)}",
            TypeString => $"STRING \"{AsString()}\"",
            TypeStringList => "STRING_LIST [" + string.Join(", ", Encoding.Unicode.GetString(Data).Split('\0', StringSplitOptions.RemoveEmptyEntries)) + "]",
            _ => $"type 0x{Type:X}, {Data.Length} bytes: {hex}",
        };
    }
}

/// <summary>Lists device nodes and reads their properties through CfgMgr32. Read-only; no admin rights needed.</summary>
internal static class DeviceNodes
{
    // cfgmgr32.h
    public const uint CrSuccess = 0x00;
    public const uint CrBufferSmall = 0x1A;
    public const uint CrNoSuchValue = 0x25;
    private const uint FilterEnumeratorPresent = 0x1 | 0x100; // CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT
    private const uint LocateDevNodeNormal = 0x0;
    private const int MaxAttempts = 3;

    // devpkey.h
    public static readonly DevPropKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    public static readonly DevPropKey DeviceDescription = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    public static readonly DevPropKey ClassGuid = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 10);
    public static readonly DevPropKey Parent = new(new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7"), 8);
    public static readonly DevPropKey ContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

    /// <summary>
    /// Instance IDs of the present devices one enumerator created, e.g. "BTHENUM". Empty when the enumerator does not
    /// exist (e.g. no Bluetooth radio).
    /// </summary>
    /// <exception cref="IOException">The list could not be read.</exception>
    public static IReadOnlyList<string> ListPresent(string enumerator)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out uint length, enumerator, FilterEnumeratorPresent) != CrSuccess)
            {
                return [];
            }

            var buffer = new char[length];
            uint result = CM_Get_Device_ID_ListW(enumerator, buffer, length, FilterEnumeratorPresent);
            if (result == CrBufferSmall)
            {
                continue; // a device arrived between the two calls
            }

            if (result != CrSuccess)
            {
                throw new IOException($"CfgMgr32 failed to list {enumerator} devices (CONFIGRET 0x{result:X2}).");
            }

            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }

        throw new IOException($"The {enumerator} device list kept growing while it was read.");
    }

    /// <summary>Finds a present device node. False when it has gone (e.g. disconnected since it was listed).</summary>
    public static bool TryLocate(string instanceId, out uint node) =>
        CM_Locate_DevNodeW(out node, instanceId, LocateDevNodeNormal) == CrSuccess;

    public static DeviceProperty Read(uint node, DevPropKey key)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            uint size = 0;
            uint result = CM_Get_DevNode_PropertyW(node, key, out uint type, null, ref size, 0);
            if (result != CrBufferSmall)
            {
                return new DeviceProperty(result, type, []); // CR_NO_SUCH_VALUE when unset; CR_SUCCESS for an empty value
            }

            var buffer = new byte[size];
            result = CM_Get_DevNode_PropertyW(node, key, out type, buffer, ref size, 0);
            if (result == CrBufferSmall)
            {
                continue; // the value grew between the two calls
            }

            return result == CrSuccess
                ? new DeviceProperty(result, type, buffer.AsSpan(0, (int)size).ToArray())
                : new DeviceProperty(result, type, []);
        }

        return new DeviceProperty(CrBufferSmall, 0, []);
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint bufferLength, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(uint devInst, in DevPropKey propertyKey, out uint propertyType, byte[]? propertyBuffer, ref uint propertyBufferSize, uint flags);
}
