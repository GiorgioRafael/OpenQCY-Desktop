using System.Buffers.Binary;

namespace OpenQCY_Desktop.Protocol;

public static class QcyN65Equalizer
{
    public const byte CustomPreset = 0x81;
    public const int MaximumBands = 10;
    public const ushort MinimumFrequency = 20;
    public const ushort MaximumFrequency = 20000;
    public const double MinimumGain = -15;
    public const double MaximumGain = 15;
    public const double MinimumQ = 0.3;
    public const double MaximumQ = 20;
    public const byte PeakFilter = 0x02;

    private const double SampleRate = 48000;
    private const double MasterGainCeiling = -14;
    private const double MasterGainMinimum = -40;
    private const double PeakHeadroom = 5;

    // Frequencies the official app uses for bands added to a curve.
    private static readonly ushort[] NewBandFrequencies = [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    // Band data captured from the official app on firmware 2.0.13, in the order the app lists the presets.
    public static IReadOnlyList<(string Name, byte Id, string Data)> Presets { get; } =
    [
        ("Default", 0x01, "0188FA1400B4FB500002A00024FA3C0002EE02D4FE6400025E0138FF640002540B1405500002401F2C012C010210275802960005"),
        ("Superior quality", 0x13, "1350FB1400B4FB50000296001EFB280002EE029CFF960002400138FF640002540BB603500002D0076400C80002102758029600057017D4FE2C0102"),
        ("Pop", 0x02, "0288FA1400ECFA460002A000C0F93C0002EE0238FF6400025E0170FE640002540B1405500002401F2C012C0102102758029600053200D4FE460002"),
        ("Bass boost", 0x03, "0388FA1400E0FC460002A00050FB3C0002EE0238FF6400025E0138FF640002540BE803500002401F2C012C01021027900196000532002C01460002"),
        ("Rock", 0x04, "0488FA1400A8FD460002A0005CF9500002EE02D4FE6400025E0138FF640002540B1405500002401F58022C0102102758029600053C002C01460002"),
        ("Soft", 0x05, "0588FA1400ECFA460002A000C0F93C0002EE0238FF6400025E0170FE640002540B8403500002401F64002C010210275802960005320038FF460002"),
        ("Classical", 0x06, "0688FA140070FE500002A000B4FB3C0002EE02D4FE6400025E0138FF640002540B1405500002401F2C012C0102102758029600053200C800460002"),
        ("Pure vocals", 0x11, "1188FA1400E0FC500002A000B4FB280002EE029CFF6400025E010000640002540B1405500002401F2C012C0102102758029600053200A8FD320002"),
        ("Original sound", 0x10, "1088FA1400D4FE460002A00018FC3C0002EE02B4FB6400025E0138FF640002540BDC05500002401F58022C01021027BC0296000532002C01460002"),
        ("Soft immersion", 0x0F, "0F88FA1400E0FC460002A0005CF9640002EE02A8FD5000025E0150FB3C0002540B2003C80002401F2C012C01021027900196000532002C01460002"),
        ("Game", 0x0D, "0D88FA1400B4FB460002A000C0F93C0002EE0238FF6400025E0170FE640002540B7805460002401F2C012C01021027580296000532006AFF460002"),
    ];

    // The default preset is the factory tuning, which the official app also uses as the starting custom curve.
    public static QcyEqualizerState FactoryTuning { get; } = GetPreset(0x01)!;

    public static QcyEqualizerState? GetPreset(byte presetId)
    {
        var preset = Presets.FirstOrDefault(item => item.Id == presetId);
        return preset.Data is null ? null : QcyEqualizerState.ParseV2(Convert.FromHexString(preset.Data));
    }

    public static byte[] SetPreset(byte presetId)
    {
        var preset = Presets.FirstOrDefault(item => item.Id == presetId);
        if (preset.Data is null)
        {
            throw new ArgumentOutOfRangeException(nameof(presetId), $"Unknown N65 preset 0x{presetId:X2}.");
        }

        return QcyPacket.Pack(0x22, Convert.FromHexString(preset.Data));
    }

    public static QcyEqualizerBand NewBand(IReadOnlyList<QcyEqualizerBand> existing)
    {
        var frequency = NewBandFrequencies.FirstOrDefault(
            candidate => existing.All(band => band.Frequency != candidate),
            (ushort)1000);
        return new QcyEqualizerBand(frequency, 0, 0.7, PeakFilter);
    }

    public static QcyEqualizerBand Clamp(QcyEqualizerBand band) => band with
    {
        Frequency = Math.Clamp(band.Frequency, MinimumFrequency, MaximumFrequency),
        Gain = Math.Round(Math.Clamp(band.Gain, MinimumGain, MaximumGain), 2),
        Q = Math.Round(Math.Clamp(band.Q, MinimumQ, MaximumQ), 2),
    };

    public static byte[] BuildCustom(IReadOnlyList<QcyEqualizerBand> bands)
    {
        if (bands.Count is 0 or > MaximumBands)
        {
            throw new ArgumentException(
                $"The N65 equalizer requires between 1 and {MaximumBands} bands.",
                nameof(bands));
        }

        var clamped = bands.Select(Clamp).ToArray();
        var parameters = new byte[3 + clamped.Length * 7];
        parameters[0] = CustomPreset;
        BinaryPrimitives.WriteInt16LittleEndian(parameters.AsSpan(1, 2), ToCentibels(MasterGain(clamped)));
        var offset = 3;
        foreach (var band in clamped)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(parameters.AsSpan(offset, 2), band.Frequency);
            BinaryPrimitives.WriteInt16LittleEndian(parameters.AsSpan(offset + 2, 2), ToCentibels(band.Gain));
            BinaryPrimitives.WriteUInt16LittleEndian(parameters.AsSpan(offset + 4, 2), (ushort)Math.Round(band.Q * 100));
            parameters[offset + 6] = band.Type;
            offset += 7;
        }

        return QcyPacket.Pack(0x22, parameters);
    }

    // The official app lowers the master gain to 5 dB below the curve's peak, and never above -14 dB.
    // This reproduces 52 of the 59 captured custom curves exactly; the others come out quieter, never louder.
    public static double MasterGain(IReadOnlyList<QcyEqualizerBand> bands)
    {
        var peak = double.MinValue;
        for (var step = 0; step < 400; step++)
        {
            var frequency = MinimumFrequency * Math.Pow((double)MaximumFrequency / MinimumFrequency, step / 399d);
            peak = Math.Max(peak, bands.Sum(band => ResponseDecibels(band, frequency)));
        }

        return Math.Round(Math.Clamp(-(peak + PeakHeadroom), MasterGainMinimum, MasterGainCeiling), 2);
    }

    private static double ResponseDecibels(QcyEqualizerBand band, double frequency)
    {
        // RBJ audio EQ cookbook biquads; the factory-only type 05 behaves like a high shelf.
        var a = Math.Pow(10, band.Gain / 40);
        var w0 = 2 * Math.PI * band.Frequency / SampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * band.Q);
        var shelf = 2 * Math.Sqrt(a) * alpha;
        double[] b, d;
        switch (band.Type)
        {
            case 0x00:
                b = [(1 + cos) / 2, -(1 + cos), (1 + cos) / 2];
                d = [1 + alpha, -2 * cos, 1 - alpha];
                break;
            case 0x01:
                b = [(1 - cos) / 2, 1 - cos, (1 - cos) / 2];
                d = [1 + alpha, -2 * cos, 1 - alpha];
                break;
            case 0x03 or 0x05:
                b = [a * ((a + 1) + (a - 1) * cos + shelf), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - shelf)];
                d = [(a + 1) - (a - 1) * cos + shelf, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - shelf];
                break;
            case 0x04:
                b = [a * ((a + 1) - (a - 1) * cos + shelf), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - shelf)];
                d = [(a + 1) + (a - 1) * cos + shelf, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - shelf];
                break;
            default:
                b = [1 + alpha * a, -2 * cos, 1 - alpha * a];
                d = [1 + alpha / a, -2 * cos, 1 - alpha / a];
                break;
        }

        var w = 2 * Math.PI * frequency / SampleRate;
        var z1 = System.Numerics.Complex.FromPolarCoordinates(1, -w);
        var z2 = z1 * z1;
        var response = (b[0] + b[1] * z1 + b[2] * z2) / (d[0] + d[1] * z1 + d[2] * z2);
        return 20 * Math.Log10(response.Magnitude);
    }

    private static short ToCentibels(double decibels) =>
        (short)Math.Clamp((int)Math.Round(decibels * 100), short.MinValue, short.MaxValue);
}
