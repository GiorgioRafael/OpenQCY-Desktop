namespace OpenQCY_Desktop.Bluetooth;

public sealed record BluetoothDeviceInfo(
    string Id,
    string Name,
    ulong BluetoothAddress,
    ulong? ControlAddress,
    ulong? OtherAddress,
    ushort VendorId,
    short SignalStrength,
    byte LeftBattery,
    byte RightBattery,
    byte CaseBattery,
    bool LeftCharging,
    bool RightCharging,
    bool CaseCharging,
    DateTimeOffset LastSeen);
