// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;

namespace OpenInzone.Tests.Protocol;

/// <summary>
/// A transport hands back whatever arrived, so a packet can come in pieces, several can come at once, and
/// the stream can begin in the middle of one. The H9 / H7 dongle's serial port does all three.
/// </summary>
public class HciReassemblerTests
{
    private static byte[] Reply(EventId eventId, ushort transactionId, params byte[] param) => new HciPacket
    {
        PacketType = HciPacketType.Event,
        Source = DeviceAddress.Receiver,
        Destination = DeviceAddress.Pc,
        EventId = eventId,
        EventType = EventType.Ret,
        TransactionId = transactionId,
        Param = param,
    }.ToArray();

    [Fact]
    public void A_reply_split_across_reads_comes_out_whole_after_stray_bytes()
    {
        var reassembler = new HciReassembler();
        byte[] reply = Reply(EventId.HeadphoneVolume, 7, 0x00, 0x2A);

        Assert.Empty(reassembler.Add([0x00]));          // noise ahead of the packet
        Assert.Empty(reassembler.Add(reply[..5]));      // the packet itself, split mid-header
        var packets = reassembler.Add(reply[5..]);

        var packet = Assert.Single(packets);
        Assert.Equal(EventId.HeadphoneVolume, packet.EventId);
        Assert.Equal(7, packet.TransactionId);
        Assert.Equal([0x00, 0x2A], packet.Param);
    }

    [Fact]
    public void Two_packets_in_one_read_both_come_out_in_order()
    {
        var reassembler = new HciReassembler();
        byte[] both = [.. Reply(EventId.BatteryInfo, 1, 0x14), .. Reply(EventId.GameChatMixBalance, 2, 0x32)];

        var packets = reassembler.Add(both);

        Assert.Equal([EventId.BatteryInfo, EventId.GameChatMixBalance], packets.Select(p => p.EventId));
    }
}
