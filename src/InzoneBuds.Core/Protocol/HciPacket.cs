namespace InzoneBuds.Protocol;

/// <summary>
/// A Sony INZONE control packet. The framing borrows Bluetooth HCI's shape but the contents are vendor specific:
/// commands use opcode 0xFC00, and every packet ends with a checksum over the bytes after the HCI header.
/// </summary>
public sealed class HciPacket
{
    /// <summary>Vendor opcode carried by every command packet.</summary>
    public const ushort CommandOpcode = 0xFC00;

    /// <summary>Magic value the firmware expects in bytes 4-5; packets without it are ignored.</summary>
    public const ushort SonyKeyId = 0xC396;

    /// <summary>Command header is 4 bytes; event header is 3. Checksums start after it.</summary>
    private const int CommandHeaderLength = 4;
    private const int EventHeaderLength = 3;

    /// <summary>Minimum bytes the length field reports for a command with no parameters.</summary>
    private const int CommandDataLengthMin = 8;

    /// <summary>Largest parameter block the vendor application puts in one packet before splitting.</summary>
    public const int MaxParamPerPacket = 50;

    public HciPacketType PacketType { get; init; }
    public DeviceAddress Source { get; init; }
    public DeviceAddress Destination { get; init; }
    public EventId EventId { get; init; }
    public EventType EventType { get; init; }
    public ushort TransactionId { get; init; }
    public byte[] Param { get; init; } = [];
    public byte Checksum { get; private init; }

    public static HciPacket CreateCommand(EventId eventId, EventType eventType, DeviceAddress destination,
        ushort transactionId, ReadOnlySpan<byte> param)
    {
        return new HciPacket
        {
            PacketType = HciPacketType.Command,
            Source = DeviceAddress.Pc,
            Destination = destination,
            EventId = eventId,
            EventType = eventType,
            TransactionId = transactionId,
            Param = param.ToArray(),
        };
    }

    public byte[] ToArray()
    {
        var buffer = new byte[Param.Length + 12];
        buffer[0] = (byte)PacketType;

        if (PacketType == HciPacketType.Command)
        {
            buffer[1] = (byte)(CommandOpcode & 0xFF);
            buffer[2] = (byte)(CommandOpcode >> 8);
            buffer[3] = (byte)(CommandDataLengthMin + Param.Length);
        }
        else
        {
            buffer[1] = 0xFF;                                    // event code
            buffer[2] = (byte)(Param.Length + 9);                // event data length
            buffer[3] = 0;                                       // reserved
        }

        buffer[4] = (byte)(SonyKeyId & 0xFF);
        buffer[5] = (byte)(SonyKeyId >> 8);
        buffer[6] = (byte)(((byte)Destination << 4) | (byte)Source);
        buffer[7] = (byte)EventId;
        buffer[8] = (byte)EventType;
        buffer[9] = (byte)(TransactionId & 0xFF);
        buffer[10] = (byte)(TransactionId >> 8);
        Param.CopyTo(buffer, 11);

        int headerLength = PacketType == HciPacketType.Command ? CommandHeaderLength : EventHeaderLength;
        int sum = 0;
        for (int i = headerLength; i < buffer.Length - 1; i++) sum += buffer[i];
        buffer[^1] = (byte)(sum & 0xFF);

        return buffer;
    }

    /// <summary>Parses a complete packet. Returns null when the buffer is malformed or the checksum does not match.</summary>
    public static HciPacket? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12) return null;

        var packetType = (HciPacketType)data[0];
        if (packetType != HciPacketType.Command && packetType != HciPacketType.Event) return null;

        int headerLength = packetType == HciPacketType.Command ? CommandHeaderLength : EventHeaderLength;
        int sum = 0;
        for (int i = headerLength; i < data.Length - 1; i++) sum += data[i];
        if ((byte)(sum & 0xFF) != data[^1]) return null;

        ushort keyId = (ushort)((data[5] << 8) | data[4]);
        if (keyId != SonyKeyId) return null;

        return new HciPacket
        {
            PacketType = packetType,
            Source = (DeviceAddress)(data[6] & 0x0F),
            Destination = (DeviceAddress)(data[6] >> 4),
            EventId = (EventId)data[7],
            EventType = (EventType)data[8],
            TransactionId = (ushort)((data[10] << 8) | data[9]),
            Param = data[11..^1].ToArray(),
            Checksum = data[^1],
        };
    }

    /// <summary>
    /// Total packet length declared by the header, or null when <paramref name="data"/> is still too short to tell.
    /// </summary>
    public static int? PeekLength(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1) return null;
        return (HciPacketType)data[0] switch
        {
            // command: 4-byte header, then data[3] more bytes
            HciPacketType.Command => data.Length < 4 ? null : CommandHeaderLength + data[3],
            // event: 3-byte header, then data[2] more bytes
            HciPacketType.Event => data.Length < 3 ? null : EventHeaderLength + data[2],
            _ => -1, // unrecognised: caller should resynchronise
        };
    }

    public override string ToString() =>
        $"{PacketType} {EventId} {EventType} tid={TransactionId} param=[{Convert.ToHexString(Param)}]";
}
