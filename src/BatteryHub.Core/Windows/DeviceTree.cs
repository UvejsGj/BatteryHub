using System.Runtime.InteropServices;
using System.Text;
using BatteryHub.Core.Hid;

namespace BatteryHub.Core.Windows;

/// <summary>Reads a device's ancestry from the Windows device tree through CfgMgr32.</summary>
internal static class DeviceTree
{
    private const string RootInstanceId = @"HTREE\ROOT\0";
    private const int MaxDepth = 64;

    private const uint CrSuccess = 0x00;
    private const uint CrBufferSmall = 0x1A;
    private const uint LocateDevNodePhantom = 0x1;
    private const int MaxDeviceIdLength = 200;

    private static readonly DevPropKey InstanceIdKey = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);
    private static readonly DevPropKey HardwareIdsKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 3);

    /// <summary>
    /// The device behind <paramref name="interfacePath"/> first, then each parent, ending with the node just below the root.
    /// </summary>
    /// <exception cref="IOException">The device tree could not be read.</exception>
    public static IReadOnlyList<DeviceNode> GetAncestry(string interfacePath)
    {
        string instanceId = GetInterfaceInstanceId(interfacePath);
        var chain = new List<DeviceNode>();
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            Check(CM_Locate_DevNodeW(out uint node, instanceId, LocateDevNodePhantom), $"locate {instanceId}");
            chain.Add(new DeviceNode(instanceId, GetHardwareIds(node)));

            Check(CM_Get_Parent(out uint parent, node, 0), $"parent of {instanceId}");
            string parentId = GetDeviceId(parent);
            if (parentId.Equals(RootInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return chain;
            }

            instanceId = parentId;
        }

        throw new IOException($"Device tree deeper than {MaxDepth} levels above {interfacePath}.");
    }

    private static string GetInterfaceInstanceId(string interfacePath)
    {
        uint size = 0;
        uint result = CM_Get_Device_Interface_PropertyW(interfacePath, InstanceIdKey, out _, null, ref size, 0);
        if (result != CrBufferSmall)
        {
            Check(result, $"instance ID of {interfacePath}");
        }

        var buffer = new byte[size];
        Check(CM_Get_Device_Interface_PropertyW(interfacePath, InstanceIdKey, out _, buffer, ref size, 0), $"instance ID of {interfacePath}");
        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    private static string GetDeviceId(uint node)
    {
        var buffer = new char[MaxDeviceIdLength + 1];
        Check(CM_Get_Device_IDW(node, buffer, (uint)buffer.Length, 0), "device ID");
        return new string(buffer).TrimEnd('\0');
    }

    // REG_MULTI_SZ: NUL-separated strings ending in an empty one. Missing hardware IDs are normal (e.g. some buses).
    private static string[] GetHardwareIds(uint node)
    {
        uint size = 0;
        if (CM_Get_DevNode_PropertyW(node, HardwareIdsKey, out _, null, ref size, 0) != CrBufferSmall)
        {
            return [];
        }

        var buffer = new byte[size];
        if (CM_Get_DevNode_PropertyW(node, HardwareIdsKey, out _, buffer, ref size, 0) != CrSuccess)
        {
            return [];
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)size).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static void Check(uint result, string what)
    {
        if (result != CrSuccess)
        {
            throw new IOException($"CfgMgr32 failed to read {what} (CONFIGRET 0x{result:X2}).");
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_PropertyW(string deviceInterface, in DevPropKey propertyKey, out uint propertyType, byte[]? propertyBuffer, ref uint propertyBufferSize, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_IDW(uint devInst, [Out] char[] buffer, uint bufferLength, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(uint devInst, in DevPropKey propertyKey, out uint propertyType, byte[]? propertyBuffer, ref uint propertyBufferSize, uint flags);
}
