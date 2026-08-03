namespace OpenQCY_Desktop.Bluetooth;

public sealed record BluetoothDeviceInfo(
    string Id,
    string Name,
    bool IsPaired,
    bool IsConnected);
