namespace OpenQCY_Desktop.Bluetooth;

public sealed record BluetoothBatteryInfo(
    string DeviceName,
    byte Percentage,
    bool IsConnected);
