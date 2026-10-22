// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using OpenInzone.Hid;
using OpenInzone.Protocol;
using OpenInzone.Serial;

namespace OpenInzone.Tests.Serial;

/// <summary>
/// The H9 / H7 dongle hands packets over as a raw byte stream, so a reply can arrive in any number of
/// pieces and with stray bytes in front. These pin that <see cref="HciSession"/> copes, without hardware.
/// </summary>
public class SerialTransportTests
{
    /// <summary>Answers every command with a canned reply, delivered a few bytes at a time.</summary>
    private sealed class FakeTransport : IHciTransport
    {
        private readonly BlockingCollection<byte[]> _incoming = [];

        public void Write(ReadOnlySpan<byte> payload)
        {
            var request = HciPacket.Parse(payload)!;
            var reply = new HciPacket
            {
                PacketType = HciPacketType.Event,
                Source = DeviceAddress.Receiver,
                Destination = DeviceAddress.Pc,
                EventId = request.EventId,
                EventType = EventType.Ret,
                TransactionId = request.TransactionId,
                Param = [0x00, 0x2A],
            }.ToArray();

            _incoming.Add([0x00]);                // noise ahead of the packet
            _incoming.Add(reply[..5]);            // the packet itself, split mid-header
            _incoming.Add(reply[5..]);
        }

        public byte[]? Read(int timeoutMilliseconds, IntPtr cancelEvent) =>
            _incoming.TryTake(out var bytes, timeoutMilliseconds) ? bytes : null;

        public void Dispose() => _incoming.Dispose();
    }

    [Fact]
    public void A_reply_split_across_reads_still_answers_the_request()
    {
        using var session = new HciSession(new FakeTransport());

        byte[] param = session.Get(EventId.HeadphoneVolume, 2000);

        Assert.Equal([0x00, 0x2A], param);
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
