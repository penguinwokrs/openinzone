using System.Runtime.InteropServices;

namespace InzoneBuds.Native;

// Minimal Core Audio interop: just enough to find the headset's own capture endpoint
// and read or write its level, which is where INZONE Hub keeps the microphone volume.

internal enum DataFlow { Render = 0, Capture = 1, All = 2 }

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey(string formatId, int propertyId)
    {
        FormatId = new Guid(formatId);
        PropertyId = propertyId;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VarType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Pointer;
    public IntPtr Pointer2;

    private const ushort VT_LPWSTR = 31;

    public string? AsString() => VarType == VT_LPWSTR ? Marshal.PtrToStringUni(Pointer) : null;
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(DataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(DataFlow dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid interfaceId, int contextFlags, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetAt(int index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig] int GetConnectorCount(out uint count);
    [PreserveSig] int GetConnector(uint index, out IConnector connector);
    [PreserveSig] int GetSubunitCount(out uint count);
    [PreserveSig] int GetSubunit(uint index, out IntPtr subunit);
    [PreserveSig] int GetPartById(uint id, out IntPtr part);
    [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);
    [PreserveSig] int GetSignalPath(IntPtr from, IntPtr to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths,
        out IntPtr parts);
}

[ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    [PreserveSig] int GetType(out int connectorType);
    [PreserveSig] int GetDataFlow(out DataFlow dataFlow);
    [PreserveSig] int ConnectTo(IConnector connectTo);
    [PreserveSig] int Disconnect();
    [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
    [PreserveSig] int GetConnectedTo(out IConnector connectedTo);
    [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string connectorId);
    [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

internal static class CoreAudio
{
    public const int DEVICE_STATE_ACTIVE = 0x00000001;
    public const int STGM_READ = 0x00000000;
    public const int CLSCTX_ALL = 0x17;

    /// <summary>PKEY_Device_InstanceId - the PnP instance id, e.g. USB\VID_054C&amp;PID_0EC2&amp;MI_00\...</summary>
    public static PropertyKey DeviceInstanceId => new("78c34fc8-104a-4aca-9ea4-524d52996e57", 256);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant variant);
}
