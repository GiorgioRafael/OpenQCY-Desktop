namespace OpenQCY_Desktop.Protocol;

public sealed record QcyAdvertisement(
    ushort VendorId,
    byte LeftBattery,
    byte RightBattery,
    byte CaseBattery,
    bool LeftCharging,
    bool RightCharging,
    bool CaseCharging,
    ulong? ControlAddress,
    ulong? OtherAddress)
{
    public static QcyAdvertisement? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            return null;
        }

        var vendorId = (ushort)((data[0] << 8) | data[1]);
        var left = data[5];
        var right = data[6];
        var deviceCase = data[7];

        ulong? controlAddress = data.Length >= 17
            ? BuildAddress(data[12], data[11], data[13], data[16], data[15], data[14])
            : null;

        ulong? otherAddress = data.Length >= 24
            ? BuildAddress(data[19], data[18], data[20], data[23], data[22], data[21])
            : null;

        if (controlAddress == 0)
        {
            controlAddress = null;
        }

        if (otherAddress == 0 || otherAddress == controlAddress)
        {
            otherAddress = null;
        }

        return new QcyAdvertisement(
            vendorId,
            BatteryPercentage(left),
            BatteryPercentage(right),
            BatteryPercentage(deviceCase),
            (left & 0x80) != 0,
            (right & 0x80) != 0,
            (deviceCase & 0x80) != 0,
            controlAddress,
            otherAddress);
    }

    public static string FormatAddress(ulong address) =>
        string.Join(":", Enumerable.Range(0, 6)
            .Select(index => ((address >> ((5 - index) * 8)) & 0xFF).ToString("X2")));

    private static byte BatteryPercentage(byte value) =>
        (byte)Math.Min(value & 0x7F, 100);

    private static ulong BuildAddress(params byte[] bytes)
    {
        ulong result = 0;
        foreach (var value in bytes)
        {
            result = (result << 8) | value;
        }

        return result;
    }
}
