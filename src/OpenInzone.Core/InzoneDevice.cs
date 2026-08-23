// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Audio;
using OpenInzone.Hid;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone;

public sealed class SettingChangedEventArgs(EventId eventId, byte[] param) : EventArgs
{
    public EventId EventId { get; } = eventId;
    public byte[] Param { get; } = param;
}

/// <summary>
/// A connected INZONE headset, reached through its USB dongle.
/// </summary>
/// <remarks>
/// The dongle exposes several HID collections; the control channel is the vendor collection on usage page
/// 0xFF04. It is opened with sharing enabled, so this works while INZONE Hub is running — both see the
/// same notifications and neither locks the other out.
/// </remarks>
public sealed class InzoneDevice : IDisposable
{
    /// <summary>Sony's USB vendor id.</summary>
    public const ushort SonyVendorId = 0x054C;

    /// <summary>Vendor usage page of the control collection.</summary>
    public const ushort ControlUsagePage = 0xFF04;

    private const int ReportLength = 64;
    private const byte ReportId = 0x02;

    private readonly HidTransport _transport;
    private readonly HciSession _session;

    public HidDeviceInfo DeviceInfo { get; }

    /// <summary>Raised when the headset reports a change we did not ask for — someone using the earbud controls.</summary>
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    private InzoneDevice(HidDeviceInfo info)
    {
        DeviceInfo = info;
        _transport = new HidTransport(info.DevicePath, ReportLength, ReportLength, ReportId, ReportId);
        _session = new HciSession(_transport);
        _session.PacketReceived += OnPacketReceived;
    }

    /// <summary>Lists every INZONE control interface currently attached.</summary>
    public static IReadOnlyList<HidDeviceInfo> Enumerate() => HidEnumerator.Enumerate(d =>
        d.VendorId == SonyVendorId &&
        d.UsagePage == ControlUsagePage &&
        d.InputReportByteLength == ReportLength &&
        d.OutputReportByteLength == ReportLength);

    /// <summary>Opens the first INZONE dongle found, or the one at <paramref name="devicePath"/>.</summary>
    public static InzoneDevice Open(string? devicePath = null)
    {
        if (devicePath is not null)
        {
            var match = Enumerate().FirstOrDefault(d =>
                string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new InvalidOperationException($"No INZONE control interface at '{devicePath}'.");
            return new InzoneDevice(match);
        }

        var devices = Enumerate();
        if (devices.Count == 0)
            throw new InvalidOperationException(
                "No INZONE dongle found. Plug in the USB transmitter and try again.");

        return new InzoneDevice(devices[0]);
    }

    // ---- Game / chat balance -------------------------------------------------

    public MixBalance GetMixBalance() => new(_session.Get(EventId.GameChatMixBalance)[0]);

    public MixBalance SetMixBalance(int value)
    {
        byte clamped = MixBalance.Clamp(value);
        _session.Set(EventId.GameChatMixBalance, [clamped]);
        return new MixBalance(clamped);
    }

    /// <summary>Nudges the balance, clamping at the ends. Positive favours game audio.</summary>
    public MixBalance AdjustMixBalance(int delta) => SetMixBalance(GetMixBalance().Value + delta);

    // ---- Headphone volume ----------------------------------------------------

    public HeadphoneVolume GetHeadphoneVolume()
        => HeadphoneVolume.Parse(_session.Get(EventId.HeadphoneVolume));

    public HeadphoneVolume SetHeadphoneVolume(int value, bool? muted = null)
    {
        var current = GetHeadphoneVolume();
        // The percent byte is echoed back untouched: it is the headset's own reading, not a value we set.
        var next = current with { Value = HeadphoneVolume.Clamp(value), Muted = muted ?? current.Muted };
        _session.Set(EventId.HeadphoneVolume, next.ToParam());
        return next;
    }

    public HeadphoneVolume AdjustHeadphoneVolume(int delta)
    {
        var current = GetHeadphoneVolume();
        var next = current with { Value = HeadphoneVolume.Clamp(current.Value + delta) };
        _session.Set(EventId.HeadphoneVolume, next.ToParam());
        return next;
    }

    public HeadphoneVolume ToggleHeadphoneMute()
    {
        var current = GetHeadphoneVolume();
        var next = current with { Muted = !current.Muted };
        _session.Set(EventId.HeadphoneVolume, next.ToParam());
        return next;
    }

    // ---- Microphone ----------------------------------------------------------
    //
    // INZONE Hub splits this in two: the mute flag lives on the headset and travels over HID,
    // while the level is the Windows capture endpoint. Both halves are mirrored here so the
    // numbers agree with what INZONE Hub shows.

    /// <summary>How many times INZONE Hub re-sends a mute change before giving up.</summary>
    private const int MicMuteAttempts = 4;
    private const int MicMuteTimeoutMs = 2000;

    private MicEndpoint? _micEndpoint;

    public MicVolume GetMicVolume() => MicVolume.Parse(_session.Get(EventId.MicVolume));

    public MicVolume SetMicMuted(bool muted)
    {
        var next = GetMicVolume() with { Muted = muted };
        SetMicVolumeWithRetry(next);
        return next;
    }

    public MicVolume ToggleMicMute() => SetMicMuted(!GetMicVolume().Muted);

    /// <summary>
    /// Mute changes cross the wireless link, where a single write is not always enough.
    /// INZONE Hub retries, so this does too.
    /// </summary>
    private void SetMicVolumeWithRetry(MicVolume value)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                _session.Set(EventId.MicVolume, value.ToParam(), MicMuteTimeoutMs);
                return;
            }
            catch (TimeoutException) when (attempt < MicMuteAttempts)
            {
                // fall through and try again
            }
        }
    }

    /// <summary>
    /// The headset's capture endpoint, or null when Windows is not exposing one. Only a successful
    /// search is remembered: at logon the dongle can be open before Windows has enumerated the
    /// capture endpoint, and caching that "no microphone" would leave the level unusable for the
    /// whole connection. A later access searches again, so it heals once the endpoint appears.
    /// </summary>
    public MicEndpoint? Microphone
    {
        get
        {
            if (_micEndpoint is not null) return _micEndpoint;
            _micEndpoint = MicEndpoint.FindForUsbDevice(DeviceInfo.VendorId, DeviceInfo.ProductId);
            return _micEndpoint;
        }
    }

    private MicEndpoint RequireMicEndpoint() => Microphone ?? throw new InvalidOperationException(
        "Windows is not exposing a microphone for this headset, so its level cannot be read or changed.");

    /// <summary>Microphone level as a percentage, on the same 0-100 scale INZONE Hub shows.</summary>
    public int GetMicLevel() => RequireMicEndpoint().Level;

    public int SetMicLevel(int percent)
    {
        var endpoint = RequireMicEndpoint();
        endpoint.Level = percent;
        return endpoint.Level;
    }

    public int AdjustMicLevel(int delta)
    {
        var endpoint = RequireMicEndpoint();
        endpoint.Level += delta;
        return endpoint.Level;
    }

    // ---- Sidetone ------------------------------------------------------------

    public SidetoneVolume GetSidetoneVolume() => SidetoneVolume.Parse(_session.Get(EventId.SidetoneVolume));

    /// <summary>
    /// Sets how much of your own voice comes back, 0-10. The second byte is whatever the headset
    /// last reported - 0xFF on INZONE Buds - and goes back untouched, as the volume's does.
    /// </summary>
    public SidetoneVolume SetSidetoneVolume(int value)
    {
        var current = GetSidetoneVolume();
        var wanted = current with { Value = SidetoneVolume.Clamp(value) };
        _session.Set(EventId.SidetoneVolume, wanted.ToParam());
        return wanted;
    }

    // ---- Ambient sound and noise cancelling ----------------------------------

    public AmbientSetting GetAmbientSetting() =>
        AmbientSetting.Parse(_session.Get(EventId.AmbientSetting));

    /// <summary>
    /// Writes mode, level and voice focus together, because the headset carries them together.
    /// Callers change one field of a reading rather than composing one, so the level survives a
    /// mode change and voice focus survives a level change.
    /// </summary>
    public AmbientSetting SetAmbientSetting(AmbientSetting setting)
    {
        var wanted = setting with { Level = AmbientSetting.ClampLevel(setting.Level) };
        _session.Set(EventId.AmbientSetting, wanted.ToParam());
        return wanted;
    }

    // ---- One-byte settings ---------------------------------------------------
    // Each carries its own byte for on: auto power off answers 0x0F, the others 0x01. The value
    // is read before it is written so the headset's own byte goes back rather than an assumed one.

    private const byte AutoPowerOffOn = 0x0F;

    public DeviceToggle GetAutoPowerOff() =>
        DeviceToggle.Parse(_session.Get(EventId.AutoPowerOff), AutoPowerOffOn);

    public DeviceToggle SetAutoPowerOff(bool on) => SetToggle(EventId.AutoPowerOff, AutoPowerOffOn, on);

    public DeviceToggle GetVoiceGuidance() => DeviceToggle.Parse(_session.Get(EventId.Guidance), 1);

    public DeviceToggle SetVoiceGuidance(bool on) => SetToggle(EventId.Guidance, 1, on);

    public DeviceToggle GetBluetoothAutoSwitch() =>
        DeviceToggle.Parse(_session.Get(EventId.IncomingPermission), 1);

    public DeviceToggle SetBluetoothAutoSwitch(bool on) => SetToggle(EventId.IncomingPermission, 1, on);

    private DeviceToggle SetToggle(EventId eventId, byte onValue, bool on)
    {
        var wanted = new DeviceToggle(on ? onValue : (byte)0, onValue);
        _session.Set(eventId, wanted.ToParam());
        return wanted;
    }

    // ---- Voice guidance language ---------------------------------------------

    public VoiceGuidanceLanguage GetVoiceGuidanceLanguage() =>
        (VoiceGuidanceLanguage)_session.Get(EventId.VoicePromptLanguage)[0];

    public VoiceGuidanceLanguage SetVoiceGuidanceLanguage(VoiceGuidanceLanguage language)
    {
        _session.Set(EventId.VoicePromptLanguage, [(byte)language]);
        return language;
    }

    // ---- Status --------------------------------------------------------------

    public BatteryInfo GetBattery() => BatteryInfo.Parse(_session.Get(EventId.BatteryInfo));

    public ModelInfo GetModelInfo() => ModelInfo.Parse(_session.Get(EventId.ModelInfo));

    /// <summary>Escape hatch for settings this wrapper does not model yet.</summary>
    public HciSession Session => _session;

    private void OnPacketReceived(object? sender, PacketReceivedEventArgs e)
    {
        if (e.Packet.PacketType != HciPacketType.Event) return;
        SettingChanged?.Invoke(this, new SettingChangedEventArgs(e.Packet.EventId, e.Packet.Param));
    }

    public void Dispose()
    {
        _session.PacketReceived -= OnPacketReceived;
        _session.Dispose();
        _transport.Dispose();
        _micEndpoint?.Dispose();
    }
}
