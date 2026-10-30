// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OpenInzone.Hid;
using OpenInzone.Native;

namespace OpenInzone.Serial;

/// <summary>Finds the USB serial ports of INZONE dongles that carry their control channel over one.</summary>
internal static partial class SerialEnumerator
{
    /// <summary>GUID_DEVINTERFACE_COMPORT: every present serial port registers an interface of this class.</summary>
    private static readonly Guid ComPortClass = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");

    [GeneratedRegex(@"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex UsbId();

    /// <summary>
    /// The interface path is what gets opened, so the COM number never matters. It embeds the USB ids
    /// (<c>\\?\usb#vid_054c&amp;pid_0e53&amp;mi_06#...</c>), which is how the dongle is picked out of the ports.
    /// </summary>
    public static IReadOnlyList<HidDeviceInfo> Enumerate(ushort vendorId, IReadOnlyCollection<ushort> productIds, string productName)
    {
        var results = new List<HidDeviceInfo>();
        var classGuid = ComPortClass;

        IntPtr set = NativeMethods.SetupDiGetClassDevsW(ref classGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (set == NativeMethods.INVALID_HANDLE_VALUE)
            throw new InvalidOperationException($"SetupDiGetClassDevs failed (error {Marshal.GetLastWin32Error()}).");

        try
        {
            for (uint index = 0; ; index++)
            {
                var did = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref classGuid, index, ref did))
                    break; // ERROR_NO_MORE_ITEMS

                string? path = HidEnumerator.GetDevicePath(set, ref did);
                if (path is null || !TryParseUsbId(path, out ushort vid, out ushort pid)) continue;
                if (vid == vendorId && productIds.Contains(pid))
                    results.Add(new HidDeviceInfo(path, vid, pid, 0, 0, 0, 0, productName, IsSerial: true));
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }

        return results;
    }

    internal static bool TryParseUsbId(string devicePath, out ushort vendorId, out ushort productId)
    {
        var match = UsbId().Match(devicePath);
        vendorId = productId = 0;
        if (!match.Success) return false;

        vendorId = Convert.ToUInt16(match.Groups[1].Value, 16);
        productId = Convert.ToUInt16(match.Groups[2].Value, 16);
        return true;
    }
}
