using System.Buffers.Binary;

namespace OpenQCY_Desktop.Protocol;

public enum QcyNoiseMode
{
    NoiseCancellation,
    Transparency,
    Normal,
}

public sealed record QcyWearingDetection(
    bool Enabled,
    byte MusicAction,
    byte AncAction,
    bool? ToneEnabled)
{
    public static QcyWearingDetection? Parse(ReadOnlySpan<byte> parameters)
    {
        if (parameters.Length is not (3 or 4))
        {
            return null;
        }

        return new QcyWearingDetection(
            parameters[0] == 0x01,
            parameters[1],
            parameters[2],
            parameters.Length == 4 ? parameters[3] == 0x01 : null);
    }
}

public static class QcyCommands
{
    public const byte InEarDetectionOpcode = 0x06;
    public const byte WearingDetectionOpcode = 0x2C;

    public static byte[] Request(byte opcode) => QcyPacket.Pack(0xFE, [opcode]);

    public static byte[] SetInEarDetection(bool enabled) =>
        Toggle(InEarDetectionOpcode, enabled);

    public static byte[] SetWearingDetection(bool enabled, QcyWearingDetection current)
    {
        var state = enabled ? (byte)0x01 : (byte)0x02;
        return current.ToneEnabled switch
        {
            true => QcyPacket.Pack(WearingDetectionOpcode, [state, current.MusicAction, current.AncAction, 0x01]),
            false => QcyPacket.Pack(WearingDetectionOpcode, [state, current.MusicAction, current.AncAction, 0x02]),
            null => QcyPacket.Pack(WearingDetectionOpcode, [state, current.MusicAction, current.AncAction]),
        };
    }

    public static byte[] SetNoiseMode(QcyNoiseMode mode)
    {
        // Defaults published for N70 vendor IDs 23872/23877.
        ReadOnlySpan<byte> parameters = mode switch
        {
            QcyNoiseMode.NoiseCancellation => [0x01, 0x05, 0x00], // adaptive ANC
            QcyNoiseMode.Transparency => [0x03, 0x01, 0x04],
            QcyNoiseMode.Normal => [0x02, 0x00, 0x00],
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        return QcyPacket.Pack(0x17, parameters);
    }

    public static byte[] SetGameMode(bool enabled) => Toggle(0x09, enabled);
    public static byte[] SetSleepMode(bool enabled) => Toggle(0x10, enabled);
    public static byte[] SetLdac(bool enabled) => Toggle(0x23, enabled);
    public static byte[] SetMultipoint(bool enabled) => Toggle(0x24, enabled);
    public static byte[] SetWindDetection(bool enabled) => Toggle(0x2A, enabled);

    public static byte[] SetPromptVolume(double percentage) =>
        QcyPacket.Pack(0x1D, [(byte)Math.Clamp((int)Math.Round(percentage), 0, 100)]);

    public static byte[] SetAutoPowerOff(ushort minutes)
    {
        Span<byte> parameters = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(parameters, minutes);
        return QcyPacket.Pack(0x14, parameters);
    }

    public static byte[] SetEqualizerPreset(byte presetIndex) => [presetIndex];

    public static byte[] SetKeyFunctions(IReadOnlyDictionary<byte, byte> mappings)
    {
        var result = new byte[mappings.Count * 2];
        var offset = 0;
        foreach (var pair in mappings.OrderBy(pair => pair.Key))
        {
            result[offset++] = pair.Key;
            result[offset++] = pair.Value;
        }

        return result;
    }

    public static IReadOnlyDictionary<byte, byte> ParseKeyFunctions(ReadOnlySpan<byte> value)
    {
        if (value.Length % 2 != 0)
        {
            return new Dictionary<byte, byte>();
        }

        var result = new Dictionary<byte, byte>();
        for (var offset = 0; offset < value.Length; offset += 2)
        {
            result[value[offset]] = value[offset + 1];
        }

        return result;
    }

    public static byte[] BuildCustomEqualizer(IReadOnlyList<double> gains, byte presetIndex = 8)
    {
        int[] frequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
        if (gains.Count != frequencies.Length)
        {
            throw new ArgumentException("The N70 equalizer requires exactly 10 bands.", nameof(gains));
        }

        var parameters = new byte[3 + frequencies.Length * 7];
        parameters[0] = presetIndex;
        // parameters 1-2: master gain, 0.00 dB.
        var offset = 3;
        for (var index = 0; index < frequencies.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(parameters.AsSpan(offset, 2), (ushort)frequencies[index]);
            var gain = (short)Math.Clamp((int)Math.Round(gains[index] * 100), -800, 800);
            BinaryPrimitives.WriteInt16LittleEndian(parameters.AsSpan(offset + 2, 2), gain);
            BinaryPrimitives.WriteUInt16LittleEndian(parameters.AsSpan(offset + 4, 2), 100); // Q = 1.00
            parameters[offset + 6] = 0;
            offset += 7;
        }

        return QcyPacket.Pack(0x22, parameters);
    }

    private static byte[] Toggle(byte opcode, bool enabled) =>
        QcyPacket.Pack(opcode, [enabled ? (byte)0x01 : (byte)0x02]);
}
