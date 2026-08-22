// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using System.Text;
using OpenInzone.Native;

namespace OpenInzone.Hid;

/// <summary>Enumerates HID collections via SetupAPI so devices are found by capability, not by a hardcoded path.</summary>
public static class HidEnumerator
{
    public static IReadOnlyList<HidDeviceInfo> Enumerate(Func<HidDeviceInfo, bool>? predicate = null)
    {
        var results = new List<HidDeviceInfo>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);

        IntPtr set = NativeMethods.SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (set == NativeMethods.INVALID_HANDLE_VALUE)
            throw new InvalidOperationException($"SetupDiGetClassDevs failed (error {Marshal.GetLastWin32Error()}).");

        try
        {
            for (uint index = 0; ; index++)
            {
                var did = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref did))
                    break; // ERROR_NO_MORE_ITEMS

                string? path = GetDevicePath(set, ref did);
                if (path is null) continue;

                var info = Describe(path);
                if (info is null) continue;
                if (predicate is null || predicate(info)) results.Add(info);
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }

        return results;
    }

    private static string? GetDevicePath(IntPtr set, ref SpDeviceInterfaceData did)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
        if (required == 0) return null;

        IntPtr buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize is 8 on x64 and 6 on x86 (DWORD + one WCHAR, aligned).
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref did, buffer, required, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Opens the collection with no access rights, which is enough to read its capabilities.</summary>
    private static HidDeviceInfo? Describe(string path)
    {
        IntPtr handle = NativeMethods.CreateFileW(path, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == NativeMethods.INVALID_HANDLE_VALUE) return null;

        try
        {
            var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
            if (!NativeMethods.HidD_GetAttributes(handle, ref attributes)) return null;

            if (!NativeMethods.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;
            try
            {
                var caps = new HidpCaps();
                if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != 0x00110000) return null; // HIDP_STATUS_SUCCESS

                var product = new StringBuilder(256);
                if (!NativeMethods.HidD_GetProductString(handle, product, product.Capacity * 2)) product.Clear();

                return new HidDeviceInfo(path, attributes.VendorId, attributes.ProductId,
                    caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength,
                    product.ToString());
            }
            finally
            {
                NativeMethods.HidD_FreePreparsedData(preparsed);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
