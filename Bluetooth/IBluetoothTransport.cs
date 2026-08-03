namespace OpenQCY_Desktop.Bluetooth;

public interface IBluetoothTransport
{
    Task<IReadOnlyList<BluetoothDeviceInfo>> FindPairedQcyDevicesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GattServiceInfo>> ReadGattServicesAsync(string deviceId, CancellationToken cancellationToken = default);
}
