using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace OpenQCY_Desktop.Bluetooth;

/// <summary>
/// Read-only Windows BLE discovery used while the MeloBuds N70 protocol is being mapped.
/// This class does not write characteristics or change the device.
/// </summary>
public sealed class WindowsBluetoothTransport : IBluetoothTransport
{
    private static readonly string[] QcyNameMarkers = ["QCY", "MeloBuds", "N70"];

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> FindPairedQcyDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var devices = await DeviceInformation.FindAllAsync(selector);
        cancellationToken.ThrowIfCancellationRequested();

        return devices
            .Where(device => QcyNameMarkers.Any(marker =>
                device.Name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Select(device => new BluetoothDeviceInfo(
                device.Id,
                string.IsNullOrWhiteSpace(device.Name) ? "QCY device" : device.Name,
                device.Pairing.IsPaired,
                device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected) && connected is true))
            .ToArray();
    }

    public async Task<IReadOnlyList<GattServiceInfo>> ReadGattServicesAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        using var device = await BluetoothLEDevice.FromIdAsync(deviceId);
        if (device is null)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (result.Status != GattCommunicationStatus.Success)
        {
            return [];
        }

        return result.Services
            .Select(service => new GattServiceInfo(service.Uuid, service.Uuid.ToString("D")))
            .ToArray();
    }
}
