namespace OpenQCY_Desktop.Protocol;

public static class QcyUuids
{
    public static readonly Guid MainService = Guid.Parse("0000a001-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Command = Guid.Parse("00001001-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Notification = Guid.Parse("00001002-0000-1000-8000-00805f9b34fb");
    public static readonly Guid FirmwareVersion = Guid.Parse("00000007-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Battery = Guid.Parse("00000008-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Equalizer = Guid.Parse("0000000b-0000-1000-8000-00805f9b34fb");
    public static readonly Guid KeyFunctions = Guid.Parse("0000000d-0000-1000-8000-00805f9b34fb");

    public static readonly Guid JieliService = Guid.Parse("0000a002-0000-1000-8000-00805f9b34fb");
    public static readonly Guid JieliCommand = Guid.Parse("00000001-0000-1000-8000-00805f9b34fb");
    public static readonly Guid JieliNotification = Guid.Parse("00000002-0000-1000-8000-00805f9b34fb");

    public const ushort CompanyId = 0x521C;
    public const ushort N70BlackVendorId = 23872;
    public const ushort N70AlternateVendorId = 23877;
    public const ushort N65VendorId = 32894;

    public static bool IsN70(ushort vendorId) =>
        vendorId is N70BlackVendorId or N70AlternateVendorId;

    public static bool IsN65(ushort vendorId) =>
        vendorId == N65VendorId;

    public static bool IsSupported(ushort vendorId) =>
        IsN70(vendorId) || IsN65(vendorId);

    public static string ModelName(ushort vendorId) => vendorId switch
    {
        N65VendorId => "QCY MeloBuds N65",
        N70BlackVendorId or N70AlternateVendorId => "QCY MeloBuds N70",
        _ => $"QCY device ({vendorId})",
    };
}
