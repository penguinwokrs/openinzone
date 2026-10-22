// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Hid;

/// <summary>
/// A HID collection exposed by a device, as seen through the Windows HID class driver. The H9 / H7
/// dongle has no HID control channel; its serial port is described by the same record, with no usage
/// page and no report lengths (see <see cref="IsSerial"/>).
/// </summary>
public sealed record HidDeviceInfo(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportByteLength,
    ushort OutputReportByteLength,
    string ProductName)
{
    /// <summary>True for a USB serial port: a real HID collection always has a usage page.</summary>
    public bool IsSerial => UsagePage == 0;

    public override string ToString() => IsSerial
        ? $"VID_{VendorId:X4}&PID_{ProductId:X4} serial \"{ProductName}\""
        : $"VID_{VendorId:X4}&PID_{ProductId:X4} UsagePage=0x{UsagePage:X4} Usage=0x{Usage:X4} " +
          $"In={InputReportByteLength} Out={OutputReportByteLength} \"{ProductName}\"";
}
