// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using OpenInzone.Ipc;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Control;

/// <summary>
/// Owns the connection and applies actions on a worker thread, so a held-down key or a dragged
/// slider never stalls the interface. The current values are cached and kept in step with the
/// headset's own notifications, which means a repeated action costs one write rather than a read
/// and a write. Every change publishes a fresh <see cref="DeviceState"/> through
/// <see cref="StateChanged"/>, raised on a background thread — either the inzone-worker thread for
/// posted actions or the inzone-hci-reader thread for unsolicited headset changes — so subscribers
/// that touch a window must marshal it themselves.
/// </summary>
public sealed class DeviceController : IDeviceActions, IDisposable
{
    private readonly BlockingCollection<Action> _work = new(new ConcurrentQueue<Action>());
    private readonly Thread _worker;
    private readonly object _stateLock = new();

    private InzoneDevice? _device;
    private DeviceState _state = DeviceState.Disconnected;
    private DateTime _nextConnectAttempt = DateTime.MinValue;

    /// <summary>Raised after every change, from either end of the link.</summary>
    public event EventHandler<DeviceState>? StateChanged;

    /// <summary>Raised when an action could not be applied. The message is fit to show a person.</summary>
    public event EventHandler<string>? Failed;

    public DeviceState State
    {
        get { lock (_stateLock) return _state; }
    }

    public DeviceController()
    {
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "inzone-worker" };
        _worker.Start();
    }

    /// <summary>Queues work against the device. Exceptions are reported, not thrown at the caller.</summary>
    public void Post(Action<DeviceController> work) => _work.Add(() => work(this));

    private void WorkLoop()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Drop();
                try { Failed?.Invoke(this, ex.Message); }
                catch { /* a misbehaving subscriber must not kill the worker */ }
                Publish(DeviceState.Disconnected);
            }
        }
    }

    private void Publish(DeviceState state)
    {
        lock (_stateLock) _state = state;
        try { StateChanged?.Invoke(this, state); }
        catch { /* a misbehaving subscriber must not kill the worker */ }
    }

    private void Mutate(Func<DeviceState, DeviceState> change)
    {
        DeviceState next;
        lock (_stateLock) next = _state = change(_state);
        try { StateChanged?.Invoke(this, next); }
        catch { /* a misbehaving subscriber must not kill the worker */ }
    }

    private void Drop()
    {
        if (_device is null) return;
        try { _device.Dispose(); } catch { /* already gone */ }
        _device = null;
        // Back off briefly so an unplugged dongle does not spin on every keypress.
        _nextConnectAttempt = DateTime.UtcNow.AddSeconds(2);
    }

    private InzoneDevice Device()
    {
        if (_device is not null) return _device;

        if (DateTime.UtcNow < _nextConnectAttempt)
            throw new InvalidOperationException("ヘッドセットに接続されていません。");

        var device = InzoneDevice.Open();
        device.SettingChanged += OnSettingChanged;
        _device = device;

        int micLevel = 0;
        bool micLevelAvailable = device.Microphone is not null;
        if (micLevelAvailable) micLevel = device.GetMicLevel();

        Publish(new DeviceState(
            Connected: true,
            ModelName: device.GetModelInfo().Name,
            Balance: device.GetMixBalance(),
            Volume: device.GetHeadphoneVolume(),
            Mic: device.GetMicVolume(),
            MicLevel: micLevel,
            MicLevelAvailable: micLevelAvailable,
            Battery: device.GetBattery()));

        return device;
    }

    /// <summary>Keeps the cache honest when the wearer or INZONE Hub changes something.</summary>
    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
        => Mutate(state => state.Apply(e.EventId, e.Param));

    /// <summary>Connects if needed and re-reads everything. Used on startup and when the flyout opens.</summary>
    public void Refresh() => Post(_ =>
    {
        var device = Device();
        // Ask the device again rather than trusting the answer taken at connect time: at logon the
        // tray can win the race against Windows enumerating the capture endpoint. InzoneDevice
        // caches only a successful search, so asking here is what lets the slider come back to life
        // instead of reading 利用不可 for the whole connection.
        bool micLevelAvailable = device.Microphone is not null;
        Mutate(state => state with
        {
            Balance = device.GetMixBalance(),
            Volume = device.GetHeadphoneVolume(),
            Mic = device.GetMicVolume(),
            MicLevel = micLevelAvailable ? device.GetMicLevel() : 0,
            MicLevelAvailable = micLevelAvailable,
            Battery = device.GetBattery(),
        });
    });

    /// <summary>
    /// Reads the device's own answers and hands them over, on the worker like every other request.
    /// A failure raises <see cref="Failed"/> and delivers nothing, which is what the caller's
    /// timeout is for.
    /// </summary>
    public void Describe(Action<DeviceDetail> deliver) => Post(_ => deliver(IpcSnapshot.Detail(Device())));

    // ---- IDeviceActions ------------------------------------------------------
    // Each one queues; none of them block the caller. The adjusting methods connect via Device()
    // before reading State, because Device() is what publishes real values on first connect -
    // reading State first would compute the delta against DeviceState.Disconnected's defaults.
    // Even connected, the read-then-write here is not atomic against OnSettingChanged, which
    // mutates straight from the HCI reader thread rather than queueing through _work; a
    // headset-initiated change landing in that window is overwritten. This mirrors the daemon's
    // caching before it, so it is accepted rather than restructured away.

    public void AdjustBalance(int delta) => Post(_ => { Device(); SetBalanceNow(State.Balance.Value + delta); });
    public void SetBalance(int value) => Post(_ => SetBalanceNow(value));
    public void AdjustVolume(int delta) => Post(_ => { Device(); SetVolumeNow(State.Volume.Value + delta); });
    public void SetVolume(int value) => Post(_ => SetVolumeNow(value));
    public void SetMicLevel(int value) => Post(_ => SetMicLevelNow(value));

    // The level is the Windows capture endpoint, not a HID value - there is no EventId for it and
    // DeviceState.Apply never refreshes it, so the cache goes stale the moment anything else (the
    // volume mixer, INZONE Hub) moves it. Adjust it live against the endpoint instead of the cache.
    public void AdjustMicLevel(int delta) => Post(_ =>
    {
        var result = Device().AdjustMicLevel(delta);
        Mutate(state => state with { MicLevel = result });
    });

    public void ToggleMicMute() => Post(_ =>
    {
        var result = Device().ToggleMicMute();
        Mutate(state => state with { Mic = result });
    });

    // The panel dropped its headphone mute - muting the headset's own volume turned out to mean
    // little next to the Windows mixer - but the CLI still offers it, and a client that asks for
    // it must not be the one process that has to open the device itself to get it.
    public void SetVolumeMuted(bool muted) => Post(_ =>
    {
        var device = Device();
        var result = device.SetHeadphoneVolume(device.GetHeadphoneVolume().Value, muted);
        Mutate(state => state with { Volume = result });
    });

    public void ToggleVolumeMute() => Post(_ =>
    {
        var result = Device().ToggleHeadphoneMute();
        Mutate(state => state with { Volume = result });
    });

    private void SetBalanceNow(int value)
    {
        var result = Device().SetMixBalance(value);
        Mutate(state => state with { Balance = result });
    }

    private void SetVolumeNow(int value)
    {
        var result = Device().SetHeadphoneVolume(value);
        Mutate(state => state with { Volume = result });
    }

    private void SetMicLevelNow(int value)
    {
        var result = Device().SetMicLevel(value);
        Mutate(state => state with { MicLevel = result });
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _worker.Join(2000);
        _device?.Dispose();
        _work.Dispose();
    }
}
