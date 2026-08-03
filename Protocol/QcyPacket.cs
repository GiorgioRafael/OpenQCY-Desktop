namespace OpenQCY_Desktop.Protocol;

public sealed record QcyCommand(byte Opcode, byte[] Parameters);

public static class QcyPacket
{
    public static byte[] Pack(byte opcode, ReadOnlySpan<byte> parameters)
    {
        if (parameters.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "A QCY command can contain at most 255 parameter bytes.");
        }

        var bodyLength = checked(parameters.Length + 2);
        if (bodyLength > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "The framed QCY command is too large.");
        }

        var packet = new byte[bodyLength + 2];
        packet[0] = 0xFF;
        packet[1] = (byte)bodyLength;
        packet[2] = opcode;
        packet[3] = (byte)parameters.Length;
        parameters.CopyTo(packet.AsSpan(4));
        return packet;
    }

    public static IReadOnlyList<QcyCommand> Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 4 || packet[0] != 0xFF)
        {
            return [];
        }

        if (packet[1] + 2 != packet.Length)
        {
            return [];
        }

        var commands = new List<QcyCommand>();
        var offset = 2;

        while (offset < packet.Length)
        {
            if (offset + 2 > packet.Length)
            {
                return [];
            }

            var opcode = packet[offset++];
            var parameterLength = packet[offset++];
            if (offset + parameterLength > packet.Length)
            {
                return [];
            }

            commands.Add(new QcyCommand(opcode, packet.Slice(offset, parameterLength).ToArray()));
            offset += parameterLength;
        }

        return commands;
    }
}
