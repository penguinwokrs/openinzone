using System.Runtime.InteropServices;
using System.Text;

namespace InzoneBuds.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeOverlapped32
{
    public IntPtr Internal;
    public IntPtr InternalHigh;
    public uint Offset;
    public uint OffsetHigh;
    public IntPtr EventHandle;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HiddAttributes
{
    public int Size;
    public ushort VendorId;
    public ushort ProductId;
    public ushort VersionNumber;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HidpCaps
{
    public ushort Usage;
    public ushort UsagePage;
    public ushort InputReportByteLength;
    public ushort OutputReportByteLength;
    public ushort FeatureReportByteLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
    public ushort[] Reserved;
    public ushort NumberLinkCollectionNodes;
    public ushort NumberInputButtonCaps;
    public ushort NumberInputValueCaps;
    public ushort NumberInputDataIndices;
    public ushort NumberOutputButtonCaps;
    public ushort NumberOutputValueCaps;
    public ushort NumberOutputDataIndices;
    public ushort NumberFeatureButtonCaps;
    public ushort NumberFeatureValueCaps;
    public ushort NumberFeatureDataIndices;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpDeviceInterfaceData
{
    public int CbSize;
    public Guid InterfaceClassGuid;
    public int Flags;
    public IntPtr Reserved;
}

internal static class NativeMethods
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public const int ERROR_IO_PENDING = 997;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_NO_MORE_ITEMS = 259;

    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 258;

    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr handle, byte[] buffer, uint numberOfBytesToRead,
        IntPtr numberOfBytesRead, ref NativeOverlapped32 overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(IntPtr handle, byte[] buffer, uint numberOfBytesToWrite,
        IntPtr numberOfBytesWritten, ref NativeOverlapped32 overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(IntPtr handle, ref NativeOverlapped32 overlapped,
        out uint numberOfBytesTransferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateEventW(IntPtr securityAttributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ResetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(IntPtr handle, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr handle, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr preparsedData, ref HidpCaps caps);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetProductString(IntPtr handle, StringBuilder buffer, int bufferLength);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData, IntPtr detailData, uint detailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
