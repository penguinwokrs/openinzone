// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.Model;

namespace OpenInzone.Cli.Session;

/// <summary>
/// The headset as held by the daemon, reached over the local channel.
/// </summary>
/// <remarks>
/// Every operation is the same shape: send whatever changes the setting, then ask for the device's
/// own answers and decode them with the decoders a direct connection would have used. Commands
/// keep their order on the channel and the daemon applies them on one worker, so the answers that
/// come back are the ones from after the change.
///
/// Reading the whole device to answer one question is a round trip more than is strictly needed.
/// It is also the reason the output cannot drift from the direct path: there is one decoder, fed
/// with the device's own bytes, either way.
/// </remarks>
internal sealed class DaemonSession : IHeadsetSession
{
    /// <summary>
    /// A whole read is six requests, each of which the headset has 1.5 seconds to answer. This is
    /// the backstop for a daemon that stopped answering, not the normal failure path: a headset
    /// that is not there fails on the first request and the error arrives long before this.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly IpcClient _client;
    private readonly object _gate = new();
    private DeviceSnapshot _state = DeviceSnapshot.Disconnected;
    private TaskCompletionSource<DeviceDetail>? _pending;
    private string? _lastError;
    private int _errors;
    private int _errorsBeforeGivingUp;

    private DaemonSession(IpcClient client, DeviceSnapshot state)
    {
        _client = client;
        _state = state;
        _client.SnapshotReceived += (_, snapshot) => _state = snapshot;
        // A command that failed is still followed by the read asked for after it, and that answer is
        // waited for before giving up. Leaving first left it to arrive at whichever invocation
        // connected next, which then took it for its own and printed a refused command as done.
        _client.DetailReceived += (_, detail) => Complete(pending =>
        {
            if (_lastError is null) pending.TrySetResult(detail);
            else pending.TrySetCanceled();
        });
        _client.ServerError += (_, message) => Complete(pending =>
        {
            _lastError ??= message;
            // Both failing means no answer is coming: the command, then the read.
            if (++_errors >= _errorsBeforeGivingUp) pending.TrySetCanceled();
        });
    }

    /// <summary>
    /// Connects to a daemon that is already running, or returns null so the caller can open the
    /// device itself. Deliberately does not start one: a single command is not worth leaving a
    /// process behind on a machine where nothing else wanted it.
    /// </summary>
    public static IHeadsetSession? TryConnect(string? pipeName = null)
    {
        var client = new IpcClient(pipeName);
        // The hello's state is kept, not just waited for. Without it the session thought nothing was
        // connected until a later change came, so a command the daemon refused - which changes
        // nothing - was reported as earbuds that did not answer.
        var connected = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SnapshotReceived += (_, snapshot) => connected.TrySetResult(snapshot);
        client.Start();

        if (connected.Task.Wait(TimeSpan.FromSeconds(2))) return new DaemonSession(client, connected.Task.Result);

        client.Dispose();
        return null;
    }

    private void Complete(Action<TaskCompletionSource<DeviceDetail>> finish)
    {
        lock (_gate)
        {
            if (_pending is not null) finish(_pending);
        }
    }

    /// <summary>
    /// Sends <paramref name="command"/>, if there is one, then asks the daemon to read the headset
    /// and waits for the answers.
    /// </summary>
    /// <remarks>
    /// The wait is set up before the command goes. A command the daemon refuses on the spot answers
    /// with an error at once, and one that arrived before anything was waiting was lost, leaving the
    /// read after it to report the refused command as done.
    /// </remarks>
    private DeviceDetail Read(string? command = null, int value = 0)
    {
        var pending = new TaskCompletionSource<DeviceDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending = pending;
            _lastError = null;
            _errors = 0;
            _errorsBeforeGivingUp = command is null ? 1 : 2;
        }

        if ((command is not null && !_client.Send(command, value)) || !_client.Send(IpcCommands.Describe))
            throw Unreachable("the daemon is no longer connected");

        try
        {
            return pending.Task.WaitAsync(Patience).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw Unreachable(_lastError ?? "the daemon could not read the headset");
        }
        catch (TimeoutException)
        {
            throw Unreachable("the daemon did not answer");
        }
        finally
        {
            lock (_gate) _pending = null;
        }
    }

    /// <summary>
    /// A headset that is not answering has to look the same through the daemon as it does on a
    /// direct connection, where it surfaces as a timeout and the tool prints "unreachable". A
    /// failure while the daemon says it is connected is something else, and keeps its own words.
    /// </summary>
    private Exception Unreachable(string message) =>
        _state.Connected ? new InvalidOperationException(message) : new TimeoutException(message);

    private static byte[] Bytes(string base64) => Convert.FromBase64String(base64);

    private DeviceDetail Apply(string command, int value = 0) => Read(command, value);

    public ModelInfo GetModelInfo() => ModelInfo.Parse(Bytes(Read().Model));

    public BatteryInfo GetBattery() => BatteryInfo.Parse(Bytes(Read().Battery));

    public MixBalance GetMixBalance() => new(Bytes(Read().Balance)[0]);

    public MixBalance SetMixBalance(int value) =>
        new(Bytes(Apply(IpcCommands.SetBalance, value).Balance)[0]);

    public MixBalance AdjustMixBalance(int delta) =>
        new(Bytes(Apply(IpcCommands.AdjustBalance, delta).Balance)[0]);

    public HeadphoneVolume GetHeadphoneVolume() => HeadphoneVolume.Parse(Bytes(Read().Volume));

    public HeadphoneVolume SetHeadphoneVolume(int value) =>
        HeadphoneVolume.Parse(Bytes(Apply(IpcCommands.SetVolume, value).Volume));

    public HeadphoneVolume SetHeadphoneMuted(bool muted) =>
        HeadphoneVolume.Parse(Bytes(Apply(IpcCommands.SetVolumeMuted, muted ? 1 : 0).Volume));

    public HeadphoneVolume ToggleHeadphoneMute() =>
        HeadphoneVolume.Parse(Bytes(Apply(IpcCommands.ToggleVolumeMute).Volume));

    public HeadphoneVolume AdjustHeadphoneVolume(int delta) =>
        HeadphoneVolume.Parse(Bytes(Apply(IpcCommands.AdjustVolume, delta).Volume));

    public MicVolume GetMicVolume() => MicVolume.Parse(Bytes(Read().Mic));

    public MicVolume SetMicMuted(bool muted) =>
        MicVolume.Parse(Bytes(Apply(IpcCommands.SetMicMuted, muted ? 1 : 0).Mic));

    public MicVolume ToggleMicMute() =>
        MicVolume.Parse(Bytes(Apply(IpcCommands.ToggleMicMute).Mic));

    public SidetoneVolume GetSidetoneVolume() => SidetoneVolume.Parse(Bytes(Read().Sidetone));

    public int? GetMicLevel() => Read().MicLevel;

    public int SetMicLevel(int value) => Level(Apply(IpcCommands.SetMicLevel, value));

    public int AdjustMicLevel(int delta) => Level(Apply(IpcCommands.AdjustMicLevel, delta));

    /// <summary>
    /// Direct mode throws when there is no endpoint to move, and the tool reports that; the daemon
    /// answers the same situation with no level at all, so it is turned back into the same failure.
    /// </summary>
    private static int Level(DeviceDetail detail) => detail.MicLevel
        ?? throw new InvalidOperationException("This headset has no adjustable microphone level.");

    public void Dispose() => _client.Dispose();
}
