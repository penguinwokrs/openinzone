// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Hid;
using OpenInzone.Protocol;
using OpenInzone.Serial;

namespace OpenInzone.Tests.Serial;

/// <summary>
/// The H9 / H7 dongle hands packets over as a raw byte stream, so a reply can arrive in any number of
/// pieces and with stray bytes in front. These pin that the stream is cut into packets correctly,
/// without hardware and without anything that only runs on Windows.
/// </summary>
public class SerialTransportTests
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

    [Fact]
    public void Dongle_ports_are_picked_out_by_the_usb_ids_in_the_interface_path()
    {
        const string path = @"\\?\usb#vid_054c&pid_0e53&mi_06#8&90b147c&0&0006#{86e0d1e0-8089-11d0-9ce4-08003e301f73}";

        Assert.True(SerialEnumerator.TryParseUsbId(path, out ushort vendor, out ushort product));
        Assert.Equal(0x054C, vendor);
        Assert.Equal(0x0E53, product);
        Assert.False(SerialEnumerator.TryParseUsbId(@"\\?\acpi#pnp0501#1#{86e0d1e0-8089-11d0-9ce4-08003e301f73}", out _, out _));
    }

    [Fact]
    public void A_serial_port_has_no_usage_page_and_a_hid_collection_always_has_one()
    {
        var port = new HidDeviceInfo(@"\\?\usb#vid_054c&pid_0e53", 0x054C, 0x0E53, 0, 0, 0, 0, "serial");
        var hid = new HidDeviceInfo(@"\\?\hid#vid_054c&pid_0ec2", 0x054C, 0x0EC2, 0xFF04, 1, 64, 64, "Hid Interface");

        Assert.True(port.IsSerial);
        Assert.False(hid.IsSerial);
    }
}
