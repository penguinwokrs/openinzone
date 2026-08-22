using System.Collections.Concurrent;
using InzoneBuds.Model;
using InzoneBuds.Protocol;

namespace InzoneBuds.Daemon;

/// <summary>
/// Owns the connection to the headset and applies actions on a worker thread, so a held-down key never
/// stalls the message loop. Current values are cached and kept in step with the headset's own
/// notifications, which means a repeated key only costs one write instead of a read and a write.
/// </summary>
public sealed class DeviceController : IDisposable
{
    private readonly BlockingCollection<Action> _work = new(new ConcurrentQueue<Action>());
    private readonly Thread _worker;
    private readonly object _stateLock = new();

    private InzoneBudsDevice? _device;
    private byte _balance = MixBalance.Centre;
    private HeadphoneVolume _volume;
    private MicVolume _mic;
    private DateTime _nextConnectAttempt = DateTime.MinValue;

    public DeviceController()
    {
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "inzone-worker" };
        _worker.Start();
    }

    public void Post(Action action) => _work.Add(action);

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
                Console.Error.WriteLine($"  {ex.Message}");
                Drop();
            }
        }
    }

    private void Drop()
    {
        if (_device is null) return;
        try { _device.Dispose(); } catch { /* already gone */ }
        _device = null;
        // Back off briefly so an unplugged dongle does not spin on every keypress.
        _nextConnectAttempt = DateTime.UtcNow.AddSeconds(2);
    }

    private InzoneBudsDevice Device()
    {
        if (_device is not null) return _device;

        if (DateTime.UtcNow < _nextConnectAttempt)
            throw new InvalidOperationException("Headset not connected.");

        var device = InzoneBudsDevice.Open();
        device.SettingChanged += OnSettingChanged;
        _device = device;

        var model = device.GetModelInfo();
        lock (_stateLock)
        {
            _balance = device.GetMixBalance().Value;
            _volume = device.GetHeadphoneVolume();
            _mic = device.GetMicVolume();
        }

        Console.WriteLine($"Connected to {model.Name} - battery {device.GetBattery()}");
        return device;
    }

    /// <summary>Keeps the cache honest when the wearer or INZONE Hub changes something.</summary>
    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        lock (_stateLock)
        {
            switch (e.EventId)
            {
                case EventId.GameChatMixBalance when e.Param.Length >= 1:
                    _balance = e.Param[0];
                    break;
                case EventId.HeadphoneVolume when e.Param.Length >= 3:
                    _volume = HeadphoneVolume.Parse(e.Param);
                    break;
                case EventId.MicVolume when e.Param.Length >= 3:
                    _mic = MicVolume.Parse(e.Param);
                    break;
            }
        }
    }

    public void AdjustBalance(int delta) => SetBalance(ReadBalance() + delta);

    public void SetBalance(int value)
    {
        var device = Device();
        byte clamped = MixBalance.Clamp(value);
        device.SetMixBalance(clamped);
        lock (_stateLock) _balance = clamped;
        Console.WriteLine($"  balance  {new MixBalance(clamped)}");
    }

    public void AdjustVolume(int delta) => SetVolume(ReadVolume().Value + delta);

    public void SetVolume(int value)
    {
        var device = Device();
        var result = device.SetHeadphoneVolume(value);
        lock (_stateLock) _volume = result;
        Console.WriteLine($"  volume   {result}");
    }

    public void ToggleVolumeMute()
    {
        var device = Device();
        var result = device.ToggleHeadphoneMute();
        lock (_stateLock) _volume = result;
        Console.WriteLine($"  volume   {result}");
    }

    public void ToggleMicMute()
    {
        var device = Device();
        var result = device.ToggleMicMute();
        lock (_stateLock) _mic = result;
        Console.WriteLine($"  mic      {result}");
    }

    /// <summary>The level is the Windows capture endpoint, so it is read fresh rather than cached.</summary>
    public void AdjustMicLevel(int delta)
    {
        var device = Device();
        Console.WriteLine($"  mic      level {device.AdjustMicLevel(delta)}%");
    }

    public void SetMicLevel(int value)
    {
        var device = Device();
        Console.WriteLine($"  mic      level {device.SetMicLevel(value)}%");
    }

    private byte ReadBalance()
    {
        Device();
        lock (_stateLock) return _balance;
    }

    private HeadphoneVolume ReadVolume()
    {
        Device();
        lock (_stateLock) return _volume;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _worker.Join(2000);
        _device?.Dispose();
        _work.Dispose();
    }
}
