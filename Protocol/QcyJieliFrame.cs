namespace OpenQCY_Desktop.Protocol;

public sealed record QcyJieliMessage(byte Flags, byte? Status, byte Sequence, byte[] Payload)
{
    public bool IsResponse => (Flags & 0x80) == 0;

    public IReadOnlyList<QcyCommand> Commands => QcyJieliFrame.ParseCommands(Payload);
}

// Jieli-based models (MeloBuds N65) carry the QCY opcode/length/parameter blocks inside RCSP frames:
// FE DC BA <flags> FF <length, big endian> [status, responses only] <sequence> <QCY blocks> EF
public static class QcyJieliFrame
{
    private const byte CommandFlags = 0xC0;
    private const byte QcyOpcode = 0xFF;
    private const byte End = 0xEF;

    private static ReadOnlySpan<byte> Header => [0xFE, 0xDC, 0xBA];

    public static byte[] Wrap(byte sequence, ReadOnlySpan<byte> qcyPacket)
    {
        if (qcyPacket.Length < 2 || qcyPacket[0] != 0xFF || qcyPacket[1] + 2 != qcyPacket.Length)
        {
            throw new ArgumentException("Expected a framed QCY packet.", nameof(qcyPacket));
        }

        var body = qcyPacket[2..];
        var length = body.Length + 1;
        var frame = new byte[length + 8];
        Header.CopyTo(frame);
        frame[3] = CommandFlags;
        frame[4] = QcyOpcode;
        frame[5] = (byte)(length >> 8);
        frame[6] = (byte)length;
        frame[7] = sequence;
        body.CopyTo(frame.AsSpan(8));
        frame[^1] = End;
        return frame;
    }

    public static IReadOnlyList<QcyJieliMessage> Parse(ReadOnlySpan<byte> data)
    {
        var messages = new List<QcyJieliMessage>();
        var offset = 0;

        while (data.Length - offset >= 9)
        {
            var frame = data[offset..];
            if (!frame[..3].SequenceEqual(Header))
            {
                break;
            }

            var length = (frame[5] << 8) | frame[6];
            var frameLength = length + 8;
            if (length < 1 || frame.Length < frameLength || frame[frameLength - 1] != End)
            {
                break;
            }

            offset += frameLength;

            // Native Jieli commands (for example C4 before a restart) are not part of the QCY protocol.
            if (frame[4] != QcyOpcode)
            {
                continue;
            }

            var flags = frame[3];
            var body = frame.Slice(7, length);
            if ((flags & 0x80) != 0)
            {
                messages.Add(new QcyJieliMessage(flags, null, body[0], body[1..].ToArray()));
            }
            else if (body.Length >= 2)
            {
                messages.Add(new QcyJieliMessage(flags, body[0], body[1], body[2..].ToArray()));
            }
        }

        return messages;
    }

    internal static IReadOnlyList<QcyCommand> ParseCommands(byte[] payload)
    {
        if (payload.Length is 0 or > byte.MaxValue)
        {
            return [];
        }

        return QcyPacket.Parse([0xFF, (byte)payload.Length, .. payload]);
    }
}
