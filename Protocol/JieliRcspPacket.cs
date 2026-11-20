using System.Buffers.Binary;

namespace OpenQCY_Desktop.Protocol;

public sealed record JieliRcspFrame(
    bool IsCommand,
    bool WantsAnswer,
    byte Opcode,
    byte Sn,
    byte Status,
    byte[] Payload)
{
    public const byte CommandFlag = 0x80;
    public const byte AnswerWantedFlag = 0x40;
    public const byte EndByte = 0xEF;
    public static readonly byte[] Head = [0xFE, 0xDC, 0xBA];

    public byte[] ToBytes()
    {
        var paramsLength = (IsCommand ? 1 : 2) + Payload.Length;
        var totalLength = 3 + 4 + paramsLength + 1; // Head(3) + Flags(1) + Opcode(1) + Len(2) + Params + End(1)
        var buffer = new byte[totalLength];

        Head.CopyTo(buffer, 0);
        buffer[3] = (byte)((IsCommand ? CommandFlag : 0) | (WantsAnswer ? AnswerWantedFlag : 0));
        buffer[4] = Opcode;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(5, 2), (ushort)paramsLength);

        if (IsCommand)
        {
            buffer[7] = Sn;
            Payload.CopyTo(buffer.AsSpan(8));
        }
        else
        {
            buffer[7] = Status;
            buffer[8] = Sn;
            Payload.CopyTo(buffer.AsSpan(9));
        }

        buffer[^1] = EndByte;
        return buffer;
    }
}

public static class JieliRcspPacket
{
    public const byte GetTargetInfo = 0x03;
    public const byte Disconnect = 0x04;
    public const byte AdvSet = 0xC0;
    public const byte AdvGet = 0xC1;
    public const byte AdvDeviceNotify = 0xC2;
    public const byte AdvRequestOperation = 0xC4;
    public const byte Custom = 0xFF;

    public const byte AttrBattery = 0x00;
    public const byte TargetFirmware = 0x05;

    public static byte[] PackCustomCommand(byte sn, byte opcode, ReadOnlySpan<byte> parameters)
    {
        var payload = new byte[2 + parameters.Length];
        payload[0] = opcode;
        payload[1] = (byte)parameters.Length;
        parameters.CopyTo(payload.AsSpan(2));

        return new JieliRcspFrame(
            IsCommand: true,
            WantsAnswer: true,
            Opcode: Custom,
            Sn: sn,
            Status: 0,
            Payload: payload).ToBytes();
    }

    public static byte[] PackCustomRequest(byte sn, byte opcode)
    {
        // Query command in QCY custom protocol: item 0xFE with 1 parameter: the target opcode
        byte[] payload = [0xFE, 0x01, opcode];
        return new JieliRcspFrame(
            IsCommand: true,
            WantsAnswer: true,
            Opcode: Custom,
            Sn: sn,
            Status: 0,
            Payload: payload).ToBytes();
    }

    public static byte[] PackAdvGet(byte sn, uint mask)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, mask);
        return new JieliRcspFrame(
            IsCommand: true,
            WantsAnswer: true,
            Opcode: AdvGet,
            Sn: sn,
            Status: 0,
            Payload: payload).ToBytes();
    }

    public static byte[] PackTargetInfo(byte sn, uint mask)
    {
        var payload = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), mask);
        payload[4] = 0x00; // Platform 0
        return new JieliRcspFrame(
            IsCommand: true,
            WantsAnswer: true,
            Opcode: GetTargetInfo,
            Sn: sn,
            Status: 0,
            Payload: payload).ToBytes();
    }

    public static byte[] PackAnswer(byte opcode, byte sn, byte status = 0) =>
        new JieliRcspFrame(
            IsCommand: false,
            WantsAnswer: false,
            Opcode: opcode,
            Sn: sn,
            Status: status,
            Payload: []).ToBytes();

    public static IReadOnlyList<QcyCommand> ExtractCommands(JieliRcspFrame frame)
    {
        var results = new List<QcyCommand>();

        if (frame.Opcode == Custom && frame.Payload.Length >= 2)
        {
            var offset = 0;
            var payload = frame.Payload;
            while (offset + 2 <= payload.Length)
            {
                var opcode = payload[offset++];
                var len = payload[offset++];
                if (offset + len > payload.Length)
                {
                    break;
                }

                var parameters = payload.AsSpan(offset, len).ToArray();
                results.Add(new QcyCommand(opcode, parameters));
                offset += len;
            }
        }
        else if ((frame.Opcode == AdvGet || frame.Opcode == AdvDeviceNotify) && frame.Payload.Length >= 2)
        {
            // Parse LTV attributes: length (counts type), type, value
            var attributes = ParseLtvAttributes(frame.Payload);
            if (attributes.TryGetValue(AttrBattery, out var batteryData) && batteryData.Length >= 3)
            {
                results.Add(new QcyCommand(0x2F, batteryData));
            }
        }
        else if (frame.Opcode == GetTargetInfo && frame.Payload.Length >= 2)
        {
            var attributes = ParseLtvAttributes(frame.Payload);
            if (attributes.TryGetValue(TargetFirmware, out var fwData) && fwData.Length >= 2)
            {
                var code = (ushort)((fwData[0] << 8) | fwData[1]);
                byte[] ver =
                [
                    (byte)((code >> 12) & 0x0F),
                    (byte)((code >> 8) & 0x0F),
                    (byte)((code >> 4) & 0x0F),
                    (byte)(code & 0x0F),
                ];
                results.Add(new QcyCommand(0x30, ver));
            }
        }

        return results;
    }

    public static Dictionary<byte, byte[]> ParseLtvAttributes(ReadOnlySpan<byte> data)
    {
        var attributes = new Dictionary<byte, byte[]>();
        var at = 0;
        while (at + 2 <= data.Length)
        {
            var length = data[at];
            if (length < 2 || at + length > data.Length)
            {
                break;
            }

            var type = data[at + 1];
            var value = data.Slice(at + 2, length - 1).ToArray();
            attributes[type] = value;
            at += length + 1; // In JieLi LTV: length is length of (type + value), plus length byte itself = length + 1
        }

        return attributes;
    }
}

public sealed class JieliRcspParser
{
    private readonly List<byte> _buffer = [];

    public IReadOnlyList<JieliRcspFrame> Feed(ReadOnlySpan<byte> incoming)
    {
        for (var i = 0; i < incoming.Length; i++)
        {
            _buffer.Add(incoming[i]);
        }

        var frames = new List<JieliRcspFrame>();

        while (true)
        {
            var headIndex = FindHead();
            if (headIndex < 0)
            {
                // No head found. Keep up to 2 bytes in case a partial head arrived.
                if (_buffer.Count > 2)
                {
                    _buffer.RemoveRange(0, _buffer.Count - 2);
                }
                break;
            }

            if (headIndex > 0)
            {
                _buffer.RemoveRange(0, headIndex);
            }

            // Need at least 7 bytes for Header
            if (_buffer.Count < 7)
            {
                break;
            }

            var flags = _buffer[3];
            var opcode = _buffer[4];
            var paramsLength = checked((ushort)((_buffer[5] << 8) | _buffer[6]));

            var totalPacketLength = 7 + paramsLength + 1;
            if (_buffer.Count < totalPacketLength)
            {
                // Not enough bytes yet for the full frame
                break;
            }

            if (_buffer[totalPacketLength - 1] != JieliRcspFrame.EndByte)
            {
                // Frame corrupted, discard head byte and retry
                _buffer.RemoveAt(0);
                continue;
            }

            var isCommand = (flags & JieliRcspFrame.CommandFlag) != 0;
            var wantsAnswer = (flags & JieliRcspFrame.AnswerWantedFlag) != 0;

            byte sn;
            byte status = 0;
            byte[] payload;

            if (isCommand)
            {
                sn = _buffer[7];
                var payloadLength = paramsLength - 1;
                payload = payloadLength > 0
                    ? _buffer.GetRange(8, payloadLength).ToArray()
                    : [];
            }
            else
            {
                status = _buffer[7];
                sn = _buffer[8];
                var payloadLength = paramsLength - 2;
                payload = payloadLength > 0
                    ? _buffer.GetRange(9, payloadLength).ToArray()
                    : [];
            }

            frames.Add(new JieliRcspFrame(
                IsCommand: isCommand,
                WantsAnswer: wantsAnswer,
                Opcode: opcode,
                Sn: sn,
                Status: status,
                Payload: payload));

            _buffer.RemoveRange(0, totalPacketLength);
        }

        return frames;
    }

    private int FindHead()
    {
        for (var i = 0; i <= _buffer.Count - 3; i++)
        {
            if (_buffer[i] == 0xFE && _buffer[i + 1] == 0xDC && _buffer[i + 2] == 0xBA)
            {
                return i;
            }
        }

        return -1;
    }
}
