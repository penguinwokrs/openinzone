// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Hid;
using OpenInzone.Serial;

namespace OpenInzone.Tests.Serial;

/// <summary>
/// How the H9 / H7 dongle's serial port is found and told apart from a HID collection, without
/// hardware and without anything that only runs on Windows.
/// </summary>
public class SerialTransportTests
{
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
    public void A_serial_port_is_flagged_and_a_hid_collection_is_not()
    {
        var port = new HidDeviceInfo(@"\\?\usb#vid_054c&pid_0e53", 0x054C, 0x0E53, 0, 0, 0, 0, "serial", IsSerial: true);
        var hid = new HidDeviceInfo(@"\\?\hid#vid_054c&pid_0ec2", 0x054C, 0x0EC2, 0xFF04, 1, 64, 64, "Hid Interface");

        Assert.True(port.IsSerial);
        Assert.False(hid.IsSerial);
    }
}
