namespace OpenQCY_Desktop.Bluetooth;

public interface IBluetoothTransport
{
    Task<IReadOnlyList<BluetoothDeviceInfo>> FindPairedQcyDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BluetoothDeviceInfo>> ScanForQcyDevicesAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task<IBluetoothDeviceConnection> ConnectAsync(
        BluetoothDeviceInfo device,
        CancellationToken cancellationToken = default);
}
