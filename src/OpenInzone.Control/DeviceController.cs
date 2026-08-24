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
    /// <summary>How often to look again while nothing is connected.</summary>
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often to read again while something is connected, so a battery level does not sit
    /// there going stale and a link that has gone is noticed without anyone having to press
    /// something.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    // Announce says whether a failure is worth telling a person about. A heartbeat that finds
    // nothing there is ordinary; the same failure from a key press is not.
    private readonly BlockingCollection<(Action Work, bool Announce)> _work =
        new(new ConcurrentQueue<(Action, bool)>());

    private readonly Thread _worker;
    private readonly object _stateLock = new();
    private readonly Timer _heartbeat;

    private InzoneDevice? _device;
    private DeviceState _state = DeviceState.Disconnected;
    private DateTime _nextConnectAttempt = DateTime.MinValue;
    private long _lastReadTicks;
    private volatile bool _stopping;

    /// <summary>Raised after every change, from either end of the link.</summary>
    public event EventHandler<DeviceState>? StateChanged;

    /// <summary>Raised when an action could not be applied. The message is fit to show a person.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>
    /// Raised when a headset has said what it has, which is once per connection. Clients are told
    /// rather than asked, because the answer belongs to the model that is plugged in.
    /// </summary>
    public event EventHandler<DeviceCapabilities>? CapabilitiesRead;

    /// <summary>Raised after reading the settings, and after every change to one of them.</summary>
    public event EventHandler<IReadOnlyList<SettingValue>>? SettingsRead;

    /// <summary>What the connected model has, or null while nothing has said.</summary>
    public DeviceCapabilities? Capabilities { get; private set; }

    public DeviceState State
    {
        get { lock (_stateLock) return _state; }
    }

    public DeviceController()
    {
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "inzone-worker" };
        _worker.Start();
        _heartbeat = new Timer(_ => Beat(), null, ReconnectInterval, ReconnectInterval);
    }

    /// <summary>Queues work against the device. Exceptions are reported, not thrown at the caller.</summary>
    public void Post(Action<DeviceController> work) => Enqueue(work, announce: true);

    private void Enqueue(Action<DeviceController> work, bool announce)
    {
        if (_stopping) return;

        try { _work.Add((() => work(this), announce)); }
        catch (InvalidOperationException) { /* shutting down between the check and the add */ }
    }

    /// <summary>
    /// Looks again on its own, which is what makes the headset coming back out of its case reach
    /// a Stream Deck. Losing the earbuds closes the device, and with it the notifications - so
    /// without this, nothing changes until someone opens the tray's panel or presses a key, and
    /// the deck sits there showing what was true before.
    /// </summary>
    private void Beat()
    {
        if (_stopping) return;

        bool connected = State.Connected;
        var since = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastReadTicks));
        if (connected && since < RefreshInterval) return;

        // Never queue behind work already waiting. A heartbeat is the least important thing here,
        // and stacking them up would make a device that is already slow to answer slower.
        if (_work.Count > 0) return;

        Enqueue(_ => ReadEverything(), announce: false);
    }

    private void WorkLoop()
    {
        foreach (var (action, announce) in _work.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Drop();
                if (announce)
                {
                    try { Failed?.Invoke(this, ex.Message); }
                    catch { /* a misbehaving subscriber must not kill the worker */ }
                }

                Publish(DeviceState.Disconnected);
            }
        }
    }

    // Both of these say nothing when nothing has changed. The heartbeat would otherwise republish
    // the same disconnected state every few seconds, and every client would redraw for it.
    private void Publish(DeviceState state)
    {
        lock (_stateLock)
        {
            if (_state == state) return;
            _state = state;
        }

        Announce(state);
    }

    private void Mutate(Func<DeviceState, DeviceState> change)
    {
        DeviceState next;
        lock (_stateLock)
        {
            next = change(_state);
            if (_state == next) return;
            _state = next;
        }

        Announce(next);
    }

    private void Announce(DeviceState state)
    {
        try { StateChanged?.Invoke(this, state); }
        catch { /* a misbehaving subscriber must not kill the worker */ }
    }

    private void Drop()
    {
        // What the last model had says nothing about the next one, and a client that connects
        // while nothing is does better with no answer than with the previous headset's: being told
        // nothing offers everything, which is where every interface starts.
        Capabilities = null;

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

        // Asked once per connection, and asked of the headset rather than assumed: the three parts
        // it publishes say what it has, where probing setting by setting can only say what did not
        // answer in time. Every client is told, because a control for a setting the model does not
        // carry is one nobody should be offered.
        Announce(IpcSnapshot.Read(device));

        return device;
    }

    /// <summary>Keeps the cache honest when the wearer or INZONE Hub changes something.</summary>
    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
        => Mutate(state => state.Apply(e.EventId, e.Param));

    /// <summary>Connects if needed and re-reads everything. Used on startup and when the flyout opens.</summary>
    /// <remarks>
    /// The reading is published even when it has not moved. Publishing only on a change is what
    /// keeps the heartbeat from republishing the same state every few seconds and making every
    /// client redraw for it - but this is not the heartbeat, which reads without coming through
    /// here. This is a client that asked, and the answer to a question is owed whatever it says.
    ///
    /// Pressing a battery key on a deck was the case that showed it: the press asks for a re-read,
    /// and a charge that had not moved since the last one produced no answer at all, so the key sat
    /// there looking exactly as it does when nothing is working.
    /// </remarks>
    public void Refresh() => Post(_ =>
    {
        ReadEverything();
        Announce(State);
    });

    private void ReadEverything()
    {
        // Connecting reads everything and publishes it, so asking again straight afterwards is
        // twelve exchanges where six would do - and the second six land while a link that has just
        // come back is still settling, which is exactly when one of them times out and takes the
        // connection down again. A headset coming out of its case was flapping for it.
        bool alreadyConnected = _device is not null;

        var device = Device();
        if (!alreadyConnected)
        {
            Interlocked.Exchange(ref _lastReadTicks, DateTime.UtcNow.Ticks);
            return;
        }

        // Ask the device again rather than trusting the answer taken at connect time: at logon the
        // tray can win the race against Windows enumerating the capture endpoint. InzoneDevice
        // caches only a successful search, so asking here is what lets the slider come back to life
        // instead of reading 利用不可 for the whole connection.
        bool micLevelAvailable = device.Microphone is not null;
        ReviseMicLevel(micLevelAvailable);
        Mutate(state => state with
        {
            Balance = device.GetMixBalance(),
            Volume = device.GetHeadphoneVolume(),
            Mic = device.GetMicVolume(),
            MicLevel = micLevelAvailable ? device.GetMicLevel() : 0,
            MicLevelAvailable = micLevelAvailable,
            Battery = device.GetBattery(),
        });

        Interlocked.Exchange(ref _lastReadTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// Reads the device's own answers and hands them over, on the worker like every other request.
    /// A failure raises <see cref="Failed"/> and delivers nothing, which is what the caller's
    /// timeout is for.
    /// </summary>
    public void Describe(Action<DeviceDetail> deliver) => Post(_ => deliver(IpcSnapshot.Detail(Device())));

    /// <summary>Reads what the headset now says, and tells every client.</summary>
    /// <remarks>
    /// Connecting reads all of this and announces it, so asking again straight afterwards is two
    /// full readings back to back - and the second lands while a link that has just come up is
    /// still settling, which is exactly when one of them times out and takes the connection down
    /// again. The same guard <see cref="ReadEverything"/> carries, for the same reason.
    /// </remarks>
    public void ReadSettings() => Post(_ =>
    {
        bool alreadyConnected = _device is not null;
        var device = Device();
        if (alreadyConnected) Announce(IpcSnapshot.Read(device));
    });

    /// <summary>
    /// Writes one setting and reads them all back, so a window shows what the headset now says
    /// rather than what it was asked for.
    /// </summary>
    /// <remarks>
    /// One method for every setting, where there used to be one each. What a setting is - which
    /// packet it lives in, which byte of it, and what range it has - is described once in the
    /// core's catalogue, and this walks that description rather than repeating it.
    /// </remarks>
    public void SetSetting(string id, int value) => Post(_ =>
    {
        var device = Device();
        IpcSnapshot.Write(device, id, value);
        Announce(IpcSnapshot.Read(device));
    });

    /// <summary>
    /// Corrects the one capability that is not the headset's to answer.
    /// </summary>
    /// <remarks>
    /// The microphone level is a Windows capture endpoint, and at logon the dongle can be open
    /// before Windows has enumerated it. <see cref="ReadEverything"/> already asks again on every
    /// heartbeat so the panel's slider comes back to life - but the capability list is read once
    /// per connection, so without this a deck's mic-level key would stay drawn as nothing and
    /// refuse to be pressed for the whole connection, while the panel beside it worked.
    /// </remarks>
    private void ReviseMicLevel(bool available)
    {
        if (Capabilities is not { } current) return;

        var revised = current.With(FeatureIds.MicLevel, available);
        if (ReferenceEquals(revised, current)) return;

        Capabilities = revised;

        try { CapabilitiesRead?.Invoke(this, revised); }
        catch { /* a misbehaving subscriber must not kill the worker */ }
    }

    /// <summary>Tells every client what the headset has and what it now says.</summary>
    private void Announce(IpcSnapshot.DeviceReading reading)
    {
        Capabilities = reading.Capabilities;

        try { CapabilitiesRead?.Invoke(this, reading.Capabilities); }
        catch { /* a misbehaving subscriber must not kill the worker */ }

        try { SettingsRead?.Invoke(this, reading.Settings); }
        catch { /* likewise */ }
    }

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

    // Explicit rather than a toggle the caller has to work out from a state it read a moment
    // ago: `inzone mic mute` means mute, whatever it was.
    public void SetMicMuted(bool muted) => Post(_ =>
    {
        var result = Device().SetMicMuted(muted);
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
        _stopping = true;
        _heartbeat.Dispose();
        _work.CompleteAdding();
        _worker.Join(2000);
        _device?.Dispose();
        _work.Dispose();
    }
}
