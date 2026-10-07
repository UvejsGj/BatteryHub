using BatteryHub.Core.BluetoothProperty;
using BatteryHub.Core.Windows;

namespace BatteryHub.Tests.BluetoothProperty;

public class BluetoothNodeIdTests
{
    // Layouts from community dumps (SARDONYX-sard/bluetooth-battery-monitor, IvanSmir/halo-battery-rs, fastfetch).
    private const string HandsFree = @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&0001054C_PID&0D58\8&2A1C3E4F&0&A0AB51C0FFEE_C00000000";
    private const string HandsFreeBypass = @"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_HCIBYPASS_VID&0001054C_PID&0D58\8&2A1C3E4F&0&A0AB51C0FFEE_C00000000";
    private const string LowEnergy = @"BTHLE\DEV_C0FFEE123456\7&1A2B3C4D&0&c0ffee123456";
    private const string AudioGateway = @"BTHENUM\{0000111F-0000-1000-8000-00805F9B34FB}_LOCALMFG&004C\8&2A1C3E4F&0&D0C5F3112233_C00000000";
    private const string LocalService = @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_LOCALMFG&0000\7&13D4A5B&0&000000000000_00000000";
    private const string DeviceNode = @"BTHENUM\DEV_A0AB51C0FFEE\8&2A1C3E4F&0&BLUETOOTHDEVICE_A0AB51C0FFEE";

    [Theory]
    [InlineData(HandsFree, BluetoothNodeKind.HandsFree)]
    [InlineData(HandsFreeBypass, BluetoothNodeKind.HandsFree)]
    [InlineData(LowEnergy, BluetoothNodeKind.LowEnergy)]
    [InlineData(@"bthle\dev_c0ffee123456\7&1&0&c0ffee123456", BluetoothNodeKind.LowEnergy)]
    public void Classifies_hands_free_and_le_nodes(string instanceId, BluetoothNodeKind kind) =>
        Assert.Equal(kind, BluetoothNodeId.Classify(instanceId));

    [Theory]
    [InlineData(AudioGateway)] // a phone paired to the PC, not a headset
    [InlineData(DeviceNode)]
    [InlineData(@"BTHLEDEVICE\{0000180F-0000-1000-8000-00805F9B34FB}_Dev_VID&02045e_PID&0b13_REV&0509_c0ffee123456\8&1&0&0024")]
    [InlineData(@"BTHHFENUM\BthHFPAudio\8&2a1c3e4f&0&97")]
    [InlineData(@"USB\VID_054C&PID_09CC\5&1")]
    public void Ignores_other_nodes(string instanceId) => Assert.Null(BluetoothNodeId.Classify(instanceId));

    [Theory]
    [InlineData(HandsFree, 0xA0AB51C0FFEEUL)]
    [InlineData(HandsFreeBypass, 0xA0AB51C0FFEEUL)]
    [InlineData(LowEnergy, 0xC0FFEE123456UL)]
    [InlineData(AudioGateway, 0xD0C5F3112233UL)]
    public void Reads_the_remote_address_from_the_instance_id(string instanceId, ulong address)
    {
        Assert.True(BluetoothNodeId.TryGetAddress(instanceId, out ulong parsed));
        Assert.Equal(address, parsed);
    }

    [Theory]
    [InlineData(LocalService)] // the PC's own service: address 0 and no _C00000000 suffix
    [InlineData(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_X\8&1&0&000000000000_C00000000")]
    [InlineData(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_X\8&1&0&A0AB51C0FFE_C00000000")] // 11 digits
    [InlineData(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_X\8&1&0&A0AB51C0FFEG_C00000000")] // not hex
    [InlineData(@"BTHLE\DEV_C0FFEE12345\7&1&0&c0ffee12345")]
    [InlineData(@"BTHLE\DEV_C0FFEE123456")] // no instance part
    [InlineData(DeviceNode)]
    [InlineData("")]
    public void Rejects_ids_without_a_remote_address(string instanceId) =>
        Assert.False(BluetoothNodeId.TryGetAddress(instanceId, out _));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(74)]
    [InlineData(100)]
    public void Accepts_one_byte_percentages(byte value) =>
        Assert.Equal(value, BluetoothNodeId.Percent(new DeviceProperty(DeviceNodes.CrSuccess, DeviceProperty.TypeByte, [value])));

    [Fact]
    public void Rejects_values_over_100() =>
        Assert.Null(BluetoothNodeId.Percent(new DeviceProperty(DeviceNodes.CrSuccess, DeviceProperty.TypeByte, [101])));

    [Fact]
    public void Rejects_an_unset_value() =>
        Assert.Null(BluetoothNodeId.Percent(new DeviceProperty(DeviceNodes.CrNoSuchValue, 0, [])));

    [Theory]
    [InlineData(DeviceProperty.TypeUInt32, new byte[] { 50, 0, 0, 0 })] // a wider value is not the battery byte
    [InlineData(DeviceProperty.TypeBoolean, new byte[] { 50 })]
    [InlineData(DeviceProperty.TypeByte, new byte[] { 50, 0 })]
    [InlineData(DeviceProperty.TypeByte, new byte[0])]
    public void Rejects_any_other_type_or_width(uint type, byte[] data) =>
        Assert.Null(BluetoothNodeId.Percent(new DeviceProperty(DeviceNodes.CrSuccess, type, data)));

    [Theory]
    [InlineData("WH-1000XM4 Hands-Free AG", "WH-1000XM4")]
    [InlineData("  Jabra Elite 85t Hands-Free AG ", "Jabra Elite 85t")]
    [InlineData("Keyboard K380", "Keyboard K380")]
    [InlineData(" Hands-Free AG", "Hands-Free AG")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Takes_the_device_name_from_the_node_name(string? friendlyName, string? name) =>
        Assert.Equal(name, BluetoothNodeId.NameFromFriendlyName(friendlyName));

    [Theory]
    [InlineData(DeviceNodes.CrNoSuchValue, 0u, new byte[0], "not set")]
    [InlineData(0x0Du, 0u, new byte[0], "CONFIGRET 0x0D")]
    [InlineData(0u, DeviceProperty.TypeByte, new byte[] { 0x4A }, "BYTE 0x4A (74)")]
    [InlineData(0u, DeviceProperty.TypeUInt32, new byte[] { 0x20, 0, 0, 0x01 }, "UINT32 0x01000020")]
    [InlineData(0u, DeviceProperty.TypeBoolean, new byte[] { 0xFF }, "BOOLEAN true (0xFF)")]
    [InlineData(0u, DeviceProperty.TypeString, new byte[] { 0x41, 0, 0x42, 0, 0, 0 }, "STRING \"AB\"")]
    [InlineData(0u, DeviceProperty.TypeStringList, new byte[] { 0x41, 0, 0, 0, 0x42, 0, 0, 0, 0, 0 }, "STRING_LIST [A, B]")]
    [InlineData(0u, 0x1003u, new byte[] { 1, 2 }, "type 0x1003, 2 bytes: 0102")]
    public void Describes_raw_values_for_the_probe(uint result, uint type, byte[] data, string expected) =>
        Assert.Equal(expected, new DeviceProperty(result, type, data).Describe());

    [Fact]
    public void Reads_guids_and_strings_only_from_matching_types()
    {
        var guid = Guid.Parse("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c");
        Assert.Equal(guid, new DeviceProperty(0, DeviceProperty.TypeGuid, guid.ToByteArray()).AsGuid());
        Assert.Null(new DeviceProperty(0, DeviceProperty.TypeString, guid.ToByteArray()).AsGuid());
        Assert.Null(new DeviceProperty(DeviceNodes.CrNoSuchValue, DeviceProperty.TypeGuid, guid.ToByteArray()).AsGuid());
        Assert.Equal("A0AB51C0FFEE", new DeviceProperty(0, DeviceProperty.TypeString, System.Text.Encoding.Unicode.GetBytes("A0AB51C0FFEE\0")).AsString());
        Assert.Null(new DeviceProperty(0, DeviceProperty.TypeByte, [1]).AsString());
    }
}
