// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using OpenInzone.Native;

namespace OpenInzone.Audio;

/// <summary>
/// The Windows capture endpoint belonging to the headset.
/// </summary>
/// <remarks>
/// The microphone level is the one setting INZONE Hub does not keep on the headset: its slider drives
/// this endpoint through Core Audio, while the mute flag goes over HID. Matching that split is what
/// keeps this tool and INZONE Hub showing the same number.
/// </remarks>
public sealed class MicEndpoint : IDisposable
{
    private readonly IAudioEndpointVolume _volume;
    private Guid _eventContext = Guid.NewGuid();

    public string DeviceId { get; }

    private MicEndpoint(string deviceId, IAudioEndpointVolume volume)
    {
        DeviceId = deviceId;
        _volume = volume;
    }

    /// <summary>Finds the capture endpoint that belongs to the given USB device, or null if it has none.</summary>
    public static MicEndpoint? FindForUsbDevice(ushort vendorId, ushort productId)
    {
        // Endpoint ids are opaque GUIDs and PKEY_Device_InstanceId is empty on many systems, so the
        // link back to USB runs through the device topology: the connector on the other side of the
        // endpoint names the audio adapter, and that name carries the vendor and product ids.
        string needle = $"vid_{vendorId:x4}&pid_{productId:x4}";

        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (enumerator.EnumAudioEndpoints(DataFlow.Capture, CoreAudio.DEVICE_STATE_ACTIVE, out var collection) != 0)
            return null;

        collection.GetCount(out int count);
        for (int i = 0; i < count; i++)
        {
            if (collection.Item(i, out var device) != 0) continue;

            string? adapterId = GetConnectedDeviceId(device);
            if (adapterId is null) continue;
            if (!adapterId.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;

            device.GetId(out string id);
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out object instance) != 0) continue;

            return new MicEndpoint(id, (IAudioEndpointVolume)instance);
        }

        return null;
    }

    /// <summary>Walks the endpoint's topology to the adapter it is wired to, and returns that device's id.</summary>
    private static string? GetConnectedDeviceId(IMMDevice endpoint)
    {
        var topologyIid = typeof(IDeviceTopology).GUID;
        if (endpoint.Activate(ref topologyIid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out object topologyObject) != 0)
            return null;

        var topology = (IDeviceTopology)topologyObject;
        try
        {
            if (topology.GetConnector(0, out var connector) != 0) return null;
            try
            {
                return connector.GetDeviceIdConnectedTo(out string deviceId) == 0 ? deviceId : null;
            }
            finally
            {
                if (Marshal.IsComObject(connector)) Marshal.ReleaseComObject(connector);
            }
        }
        finally
        {
            if (Marshal.IsComObject(topology)) Marshal.ReleaseComObject(topology);
        }
    }

    /// <summary>Level as a percentage, 0-100, matching the scale INZONE Hub shows.</summary>
    public int Level
    {
        get
        {
            Check(_volume.GetMasterVolumeLevelScalar(out float scalar), "read the microphone level");
            return (int)Math.Round(scalar * 100f);
        }
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            Check(_volume.SetMasterVolumeLevelScalar(clamped / 100f, ref _eventContext), "set the microphone level");
        }
    }

    /// <summary>The Windows-side mute for this endpoint, which is separate from the headset's own mute.</summary>
    public bool EndpointMuted
    {
        get
        {
            Check(_volume.GetMute(out bool muted), "read the microphone mute state");
            return muted;
        }
        set => Check(_volume.SetMute(value, ref _eventContext), "set the microphone mute state");
    }

    private static void Check(int hr, string what)
    {
        if (hr != 0) throw new InvalidOperationException($"Could not {what} (HRESULT 0x{hr:X8}).");
    }

    public void Dispose()
    {
        if (Marshal.IsComObject(_volume)) Marshal.ReleaseComObject(_volume);
    }
}
