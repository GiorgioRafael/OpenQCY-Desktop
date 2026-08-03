namespace OpenQCY_Desktop.Bluetooth;

public sealed record GattCharacteristicInfo(
    Guid Uuid,
    bool CanRead,
    bool CanWrite,
    bool CanNotify);
