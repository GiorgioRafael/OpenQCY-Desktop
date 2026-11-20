namespace OpenQCY_Desktop.Protocol;

public static class QcyUuids
{
    // WuQi (N70) GATT Service & Characteristics
    public static readonly Guid MainService = Guid.Parse("0000a001-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Command = Guid.Parse("00001001-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Notification = Guid.Parse("00001002-0000-1000-8000-00805f9b34fb");
    public static readonly Guid FirmwareVersion = Guid.Parse("00000007-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Battery = Guid.Parse("00000008-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Equalizer = Guid.Parse("0000000b-0000-1000-8000-00805f9b34fb");
    public static readonly Guid KeyFunctions = Guid.Parse("0000000d-0000-1000-8000-00805f9b34fb");

    // JieLi (N30 / JL) GATT Service & Characteristics
    public static readonly Guid JieliMainService = Guid.Parse("0000a002-0000-1000-8000-00805f9b34fb");
    public static readonly Guid JieliCommand = Guid.Parse("00000001-0000-1000-8000-00805f9b34fb");
    public static readonly Guid JieliNotification = Guid.Parse("00000002-0000-1000-8000-00805f9b34fb");

    // Classic Bluetooth RFCOMM SPP Service
    public static readonly Guid SppService = Guid.Parse("00001101-0000-1000-8000-00805f9b34fb");

    public const ushort CompanyId = 0x521C;
    public const ushort N70BlackVendorId = 23872;
    public const ushort N70AlternateVendorId = 23877;
    public const ushort N30VendorId = 32881; // 0x8071 "QCY MeloBuds N30"
    public const ushort N30AlternateVendorId = 32883; // 0x8073 "QCY HT16 MeloBuds "

    public static bool IsN70(ushort vendorId) =>
        vendorId is N70BlackVendorId or N70AlternateVendorId;

    public static bool IsN30(ushort vendorId) =>
        vendorId is N30VendorId or N30AlternateVendorId;

    public static bool IsSupported(ushort vendorId) =>
        IsN70(vendorId) || IsN30(vendorId);

    public static bool IsJieli(ushort vendorId) =>
        IsN30(vendorId) || (vendorId & 0x00F0) == 0x0070 || (vendorId & 0x8070) == 0x8070;

    public static string GetModelName(ushort vendorId) => vendorId switch
    {
        N70BlackVendorId or N70AlternateVendorId => "QCY MeloBuds N70",
        N30VendorId or N30AlternateVendorId => "QCY MeloBuds N30",
        _ => $"QCY Device ({vendorId})"
    };
}
