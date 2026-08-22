// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Hid;

/// <summary>A HID collection exposed by a device, as seen through the Windows HID class driver.</summary>
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
    public override string ToString() =>
        $"VID_{VendorId:X4}&PID_{ProductId:X4} UsagePage=0x{UsagePage:X4} Usage=0x{Usage:X4} " +
        $"In={InputReportByteLength} Out={OutputReportByteLength} \"{ProductName}\"";
}
