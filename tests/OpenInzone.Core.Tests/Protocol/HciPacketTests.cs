// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;

namespace OpenInzone.Tests.Protocol;

/// <summary>
/// Byte-for-byte checks against the worked example in docs/PROTOCOL.md, which was captured from a real
/// dongle. Those four packets pin down the framing, the address packing, the little endian transaction id
/// and — the part that is easiest to get wrong — where each checksum starts.
/// </summary>
public class HciPacketTests
{
    // Reading the game/chat balance. The reply carries 0x32, i.e. 50.
    private static readonly byte[] BalanceGetCommand =
        [0x01, 0x00, 0xFC, 0x08, 0x96, 0xC3, 0x41, 0x22, 0x01, 0x01, 0x00, 0xBE];
    private static readonly byte[] BalanceGetReply =
        [0x04, 0xFF, 0x0A, 0x00, 0x96, 0xC3, 0x14, 0x22, 0x10, 0x01, 0x00, 0x32, 0xD2];

    // Setting it to 30. The answer is NTFY, not RET, and carries the same transaction id.
    private static readonly byte[] BalanceSetCommand =
        [0x01, 0x00, 0xFC, 0x09, 0x96, 0xC3, 0x41, 0x22, 0x02, 0x65, 0x00, 0x1E, 0x41];
    private static readonly byte[] BalanceSetReply =
        [0x04, 0xFF, 0x0A, 0x00, 0x96, 0xC3, 0x14, 0x22, 0x20, 0x65, 0x00, 0x1E, 0x32];

    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    // -- assembling commands ------------------------------------------------------------------

    [Fact]
    public void Assembles_the_captured_balance_get()
    {
        var packet = HciPacket.CreateCommand(EventId.GameChatMixBalance, EventType.Get,
            DeviceAddress.Receiver, transactionId: 1, []);

        Assert.Equal(Hex(BalanceGetCommand), Hex(packet.ToArray()));
    }

    [Fact]
    public void Assembles_the_captured_balance_set()
    {
        var packet = HciPacket.CreateCommand(EventId.GameChatMixBalance, EventType.Set,
            DeviceAddress.Receiver, transactionId: 0x65, [30]);

        Assert.Equal(Hex(BalanceSetCommand), Hex(packet.ToArray()));
    }

    [Fact]
    public void Sends_commands_from_the_pc()
    {
        var packet = HciPacket.CreateCommand(EventId.BatteryInfo, EventType.Get,
            DeviceAddress.Receiver, transactionId: 1, []);

        Assert.Equal(DeviceAddress.Pc, packet.Source);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(HciPacket.MaxParamPerPacket)]
    public void Command_length_byte_counts_eight_plus_the_parameter(int paramLength)
    {
        var bytes = Command(new byte[paramLength]).ToArray();

        Assert.Equal(8 + paramLength, bytes[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(HciPacket.MaxParamPerPacket)]
    public void Packet_is_twelve_bytes_plus_the_parameter(int paramLength)
    {
        var bytes = Command(new byte[paramLength]).ToArray();

        Assert.Equal(12 + paramLength, bytes.Length);
    }

    [Theory]
    [InlineData(DeviceAddress.Receiver, 0x41)]
    [InlineData(DeviceAddress.Transmitter, 0x21)]
    public void Address_byte_packs_the_destination_in_the_high_nibble(DeviceAddress destination, byte expected)
    {
        var bytes = HciPacket.CreateCommand(EventId.GameChatMixBalance, EventType.Get,
            destination, transactionId: 1, []).ToArray();

        Assert.Equal(expected, bytes[6]);
    }

    [Fact]
    public void Transaction_id_is_little_endian()
    {
        var bytes = HciPacket.CreateCommand(EventId.GameChatMixBalance, EventType.Get,
            DeviceAddress.Receiver, transactionId: 0x1234, []).ToArray();

        Assert.Equal(0x34, bytes[9]);
        Assert.Equal(0x12, bytes[10]);
    }

    [Fact]
    public void Carries_the_key_id_the_firmware_demands()
    {
        var bytes = Command([]).ToArray();

        Assert.Equal(0x96, bytes[4]);
        Assert.Equal(0xC3, bytes[5]);
    }

    // -- checksums ----------------------------------------------------------------------------

    /// <summary>
    /// A command checksum starts at index 4, so the data length byte at index 3 is outside it.
    /// Starting one byte earlier is the classic mistake, and the assertion below would catch it:
    /// index 3 is never zero on a command, so the two sums always differ.
    /// </summary>
    [Fact]
    public void Command_checksum_starts_after_the_four_byte_hci_header()
    {
        var bytes = Command([1, 2, 3]).ToArray();

        Assert.Equal(SumRange(bytes, 4), bytes[^1]);
        Assert.NotEqual(SumRange(bytes, 3), bytes[^1]);
    }

    /// <summary>
    /// An event checksum starts at index 3, one byte earlier than a command's, so it covers the
    /// reserved byte. Every event from real hardware carries zero there, which makes both start
    /// points agree; only a packet with the reserved byte set tells the two apart. This synthetic
    /// one does that, and pins the rule the specification states.
    /// </summary>
    [Fact]
    public void Event_checksum_covers_the_reserved_byte()
    {
        var bytes = (byte[])BalanceGetReply.Clone();
        bytes[3] = 0x01;
        bytes[^1] = SumRange(bytes, 3);

        Assert.NotNull(HciPacket.Parse(bytes));
    }

    [Fact]
    public void Checksum_is_truncated_to_eight_bits()
    {
        // 40 bytes of 0xFF push the running sum well past 0xFF.
        var bytes = Command(Enumerable.Repeat((byte)0xFF, 40).ToArray()).ToArray();

        Assert.Equal(SumRange(bytes, 4), bytes[^1]);
    }

    // -- parsing ------------------------------------------------------------------------------

    [Fact]
    public void Parses_the_ret_that_answers_a_get()
    {
        var packet = HciPacket.Parse(BalanceGetReply);

        Assert.NotNull(packet);
        Assert.Equal(HciPacketType.Event, packet.PacketType);
        Assert.Equal(DeviceAddress.Receiver, packet.Source);
        Assert.Equal(DeviceAddress.Pc, packet.Destination);
        Assert.Equal(EventId.GameChatMixBalance, packet.EventId);
        Assert.Equal(EventType.Ret, packet.EventType);
        Assert.Equal(1, packet.TransactionId);
        Assert.Equal([0x32], packet.Param);
    }

    /// <summary>
    /// The answer to a SET is NTFY, not RET. Anything waiting only for RET waits forever.
    /// </summary>
    [Fact]
    public void Parses_the_notify_that_answers_a_set()
    {
        var packet = HciPacket.Parse(BalanceSetReply);

        Assert.NotNull(packet);
        Assert.Equal(EventType.Notify, packet.EventType);
        Assert.NotEqual(EventType.Ret, packet.EventType);
        Assert.Equal(0x65, packet.TransactionId);
        Assert.Equal([30], packet.Param);
    }

    [Fact]
    public void Parse_keeps_the_checksum_it_verified()
    {
        var packet = HciPacket.Parse(BalanceGetReply);

        Assert.NotNull(packet);
        Assert.Equal(0xD2, packet.Checksum);
    }

    [Fact]
    public void Parse_rejects_a_bad_checksum()
    {
        var corrupt = (byte[])BalanceGetReply.Clone();
        corrupt[^1] ^= 0xFF;

        Assert.Null(HciPacket.Parse(corrupt));
    }

    [Fact]
    public void Parse_rejects_a_tampered_parameter()
    {
        var corrupt = (byte[])BalanceGetReply.Clone();
        corrupt[11] = 0x63;                 // the checksum no longer covers this value

        Assert.Null(HciPacket.Parse(corrupt));
    }

    [Fact]
    public void Parse_rejects_a_foreign_key_id()
    {
        var foreign = (byte[])BalanceGetReply.Clone();
        foreign[4] = 0x00;
        foreign[5] = 0x00;
        foreign[^1] = SumRange(foreign, 3);  // otherwise the checksum would reject it first

        Assert.Null(HciPacket.Parse(foreign));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    public void Parse_rejects_a_buffer_too_short_to_be_a_packet(int length)
    {
        Assert.Null(HciPacket.Parse(BalanceGetReply.AsSpan(0, length)));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x02)]   // the HID report id, seen when the stream is out of step
    [InlineData(0xFF)]
    public void Parse_rejects_an_unknown_packet_type(byte packetType)
    {
        var bytes = (byte[])BalanceGetReply.Clone();
        bytes[0] = packetType;

        Assert.Null(HciPacket.Parse(bytes));
    }

    [Fact]
    public void Round_trips_a_command()
    {
        var original = HciPacket.CreateCommand(EventId.HeadphoneVolume, EventType.Set,
            DeviceAddress.Receiver, transactionId: 0x4321, [1, 15, 0xFF]);

        var parsed = HciPacket.Parse(original.ToArray());

        Assert.NotNull(parsed);
        Assert.Equal(HciPacketType.Command, parsed.PacketType);
        Assert.Equal(DeviceAddress.Pc, parsed.Source);
        Assert.Equal(DeviceAddress.Receiver, parsed.Destination);
        Assert.Equal(EventId.HeadphoneVolume, parsed.EventId);
        Assert.Equal(EventType.Set, parsed.EventType);
        Assert.Equal(0x4321, parsed.TransactionId);
        Assert.Equal([1, 15, 0xFF], parsed.Param);
    }

    [Fact]
    public void Round_trips_the_largest_parameter_one_packet_carries()
    {
        var param = Enumerable.Range(0, HciPacket.MaxParamPerPacket).Select(i => (byte)i).ToArray();

        var parsed = HciPacket.Parse(Command(param).ToArray());

        Assert.NotNull(parsed);
        Assert.Equal(param, parsed.Param);
    }

    // -- framing the report stream ------------------------------------------------------------

    [Fact]
    public void Peek_length_of_a_command_is_four_plus_its_length_byte()
    {
        Assert.Equal(BalanceGetCommand.Length, HciPacket.PeekLength(BalanceGetCommand));
        Assert.Equal(BalanceSetCommand.Length, HciPacket.PeekLength(BalanceSetCommand));
    }

    [Fact]
    public void Peek_length_of_an_event_is_three_plus_its_length_byte()
    {
        Assert.Equal(BalanceGetReply.Length, HciPacket.PeekLength(BalanceGetReply));
        Assert.Equal(BalanceSetReply.Length, HciPacket.PeekLength(BalanceSetReply));
    }

    /// <summary>The length is readable before the whole packet has arrived.</summary>
    [Fact]
    public void Peek_length_reads_a_truncated_packet()
    {
        Assert.Equal(BalanceGetReply.Length, HciPacket.PeekLength(BalanceGetReply.AsSpan(0, 3)));
        Assert.Equal(BalanceGetCommand.Length, HciPacket.PeekLength(BalanceGetCommand.AsSpan(0, 4)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Peek_length_waits_for_the_command_length_byte(int available)
    {
        Assert.Null(HciPacket.PeekLength(BalanceGetCommand.AsSpan(0, available)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Peek_length_waits_for_the_event_length_byte(int available)
    {
        Assert.Null(HciPacket.PeekLength(BalanceGetReply.AsSpan(0, available)));
    }

    /// <summary>A negative answer tells the reader to drop a byte and look for the next packet.</summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x02)]
    [InlineData(0xFF)]
    public void Peek_length_is_negative_for_an_unrecognised_start_byte(byte first)
    {
        var bytes = (byte[])BalanceGetReply.Clone();
        bytes[0] = first;

        Assert.True(HciPacket.PeekLength(bytes) < 0);
    }

    // -- helpers ------------------------------------------------------------------------------

    private static HciPacket Command(byte[] param) =>
        HciPacket.CreateCommand(EventId.GameChatMixBalance, EventType.Set,
            DeviceAddress.Receiver, transactionId: 1, param);

    /// <summary>The checksum as docs/PROTOCOL.md defines it: sum from <paramref name="start"/> to the byte before the checksum.</summary>
    private static byte SumRange(byte[] packet, int start)
    {
        int sum = 0;
        for (int i = start; i < packet.Length - 1; i++) sum += packet[i];
        return (byte)(sum & 0xFF);
    }
}
