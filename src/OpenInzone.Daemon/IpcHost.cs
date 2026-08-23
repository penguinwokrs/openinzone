// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;
using OpenInzone.Ipc;
using OpenInzone.Model;

namespace OpenInzone.Daemon;

/// <summary>
/// Serves the headset to every local client: the tray's panel, the CLI, the Stream Deck plugin.
/// </summary>
/// <remarks>
/// The translation in both directions lives here rather than in OpenInzone.Ipc so that the wire
/// format stays independent of <see cref="DeviceState"/>: clients ship as their own executables
/// and may be older builds than the daemon they are talking to.
/// </remarks>
internal sealed class IpcHost : IDisposable
{
    private readonly DeviceController _controller;
    private readonly IpcServer _server;

    /// <summary>Raised when the channel cannot be served, with a message fit for a balloon.</summary>
    public event EventHandler<string>? Failed;

    public IpcHost(DeviceController controller)
    {
        _controller = controller;
        _server = new IpcServer(() => IpcSnapshot.From(controller.State));
        _server.CommandReceived += (_, message) => Execute(message);
        _server.Failed += (_, message) => Failed?.Invoke(this, message);

        // A request that could not be carried out is the client's business, not just the log's:
        // without this a describe that failed would look to the caller like a channel that had
        // simply gone quiet.
        controller.Failed += (_, message) => _server.PublishError(message);

        // Raised on the controller's worker thread. Publishing does not block on any client, so a
        // deck that has stopped reading cannot hold up the headset.
        controller.StateChanged += (_, state) => _server.Publish(IpcSnapshot.From(state));
    }

    public void Start() => _server.Start();

    /// <summary>How many clients are connected, which is what decides when the daemon may stop.</summary>
    public int ClientCount => _server.ClientCount;

    /// <summary>
    /// Commands are queued onto the controller's worker like any other request, so a client cannot
    /// interleave with the tray's own panel.
    /// </summary>
    private void Execute(ClientMessage message)
    {
        switch (message.Command)
        {
            case IpcCommands.Refresh: _controller.Refresh(); break;
            case IpcCommands.AdjustVolume: _controller.AdjustVolume(message.Value); break;
            case IpcCommands.SetVolume: _controller.SetVolume(message.Value); break;
            case IpcCommands.AdjustBalance: _controller.AdjustBalance(message.Value); break;
            case IpcCommands.SetBalance: _controller.SetBalance(message.Value); break;
            case IpcCommands.ToggleMicMute: _controller.ToggleMicMute(); break;
            case IpcCommands.AdjustMicLevel: _controller.AdjustMicLevel(message.Value); break;
            case IpcCommands.SetMicLevel: _controller.SetMicLevel(message.Value); break;
            case IpcCommands.SetMicMuted: _controller.SetMicMuted(message.Value != 0); break;
            case IpcCommands.SetVolumeMuted: _controller.SetVolumeMuted(message.Value != 0); break;
            case IpcCommands.ToggleVolumeMute: _controller.ToggleVolumeMute(); break;
            case IpcCommands.Describe: _controller.Describe(detail => _server.Publish(detail)); break;

            // Every one of these answers with the whole set read back from the headset, so a
            // window shows what the headset now says rather than what it was asked for.
            case IpcCommands.GetSettings: _controller.ReadSettings(Deliver); break;
            case IpcCommands.SetSidetone: _controller.SetSidetone(message.Value, Deliver); break;
            case IpcCommands.SetAutoPowerOff:
                _controller.SetAutoPowerOff(message.Value != 0, Deliver);
                break;
            case IpcCommands.SetVoiceGuidance:
                _controller.SetVoiceGuidance(message.Value != 0, Deliver);
                break;
            case IpcCommands.SetVoiceGuidanceLanguage:
                _controller.SetVoiceGuidanceLanguage((VoiceGuidanceLanguage)message.Value, Deliver);
                break;
            case IpcCommands.SetBluetoothAutoSwitch:
                _controller.SetBluetoothAutoSwitch(message.Value != 0, Deliver);
                break;

            // The ambient packet carries three settings at once, so each of these changes one
            // field of what the headset currently says rather than composing a whole packet.
            case IpcCommands.SetAmbientMode:
                _controller.ChangeAmbient(
                    current => current with { Mode = (AmbientMode)message.Value }, Deliver);
                break;
            case IpcCommands.SetAmbientLevel:
                _controller.ChangeAmbient(
                    current => current with { Level = AmbientSetting.ClampLevel(message.Value) }, Deliver);
                break;
            case IpcCommands.SetVoiceFocus:
                _controller.ChangeAmbient(
                    current => current with { VoiceFocus = message.Value != 0 }, Deliver);
                break;
        }
    }

    private void Deliver(DeviceSettings settings) => _server.Publish(settings);

    public void Dispose() => _server.Dispose();
}
