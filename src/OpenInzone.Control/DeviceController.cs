// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Control;

/// <summary>
/// Owns the connection and applies actions on a worker thread, so a held-down key or a dragged
/// slider never stalls the interface. The current values are cached and kept in step with the
/// headset's own notifications, which means a repeated action costs one write rather than a read
/// and a write. Every change publishes a fresh <see cref="DeviceState"/> through
/// <see cref="StateChanged"/>; the event is raised on the worker thread, so subscribers that touch
/// a window must marshal it themselves.
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
                Failed?.Invoke(this, ex.Message);
                Publish(DeviceState.Disconnected);
            }
        }
    }

    private void Publish(DeviceState state)
    {
        lock (_stateLock) _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void Mutate(Func<DeviceState, DeviceState> change)
    {
        DeviceState next;
        lock (_stateLock) next = _state = change(_state);
        StateChanged?.Invoke(this, next);
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
        Mutate(state => state with
        {
            Balance = device.GetMixBalance(),
            Volume = device.GetHeadphoneVolume(),
            Mic = device.GetMicVolume(),
            MicLevel = state.MicLevelAvailable ? device.GetMicLevel() : 0,
            Battery = device.GetBattery(),
        });
    });

    // ---- IDeviceActions ------------------------------------------------------
    // Each one queues; none of them block the caller.

    public void AdjustBalance(int delta) => Post(_ => SetBalanceNow(State.Balance.Value + delta));
    public void SetBalance(int value) => Post(_ => SetBalanceNow(value));
    public void AdjustVolume(int delta) => Post(_ => SetVolumeNow(State.Volume.Value + delta));
    public void SetVolume(int value) => Post(_ => SetVolumeNow(value));
    public void SetMicLevel(int value) => Post(_ => SetMicLevelNow(value));
    public void AdjustMicLevel(int delta) => Post(_ => SetMicLevelNow(State.MicLevel + delta));

    public void ToggleVolumeMute() => Post(_ =>
    {
        var result = Device().ToggleHeadphoneMute();
        Mutate(state => state with { Volume = result });
    });

    public void ToggleMicMute() => Post(_ =>
    {
        var result = Device().ToggleMicMute();
        Mutate(state => state with { Mic = result });
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
