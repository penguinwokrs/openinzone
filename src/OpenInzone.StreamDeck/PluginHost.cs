// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using OpenInzone.Ipc;

namespace OpenInzone.StreamDeck;

/// <summary>
/// Joins the two conversations: events from the Stream Deck application, and state from the tray.
/// </summary>
/// <remarks>
/// Nothing here opens the headset. Commands go to the tray, which owns the device, and the deck is
/// redrawn from the snapshots the tray pushes back - including snapshots caused by someone moving
/// a slider in the tray's own panel, which is what keeps the two surfaces agreeing.
/// </remarks>
internal sealed class PluginHost : IDisposable
{
    /// <summary>One key or dial the user has placed on a deck.</summary>
    private sealed record Instance(string ActionId, ActionSettings Settings, bool IsEncoder);

    /// <summary>
    /// How long a directed key shows what a press did. Long enough to read at a glance, short
    /// enough that a key you have finished with is a picture again before you look back at it.
    /// </summary>
    private static readonly TimeSpan Moment = TimeSpan.FromSeconds(1.5);

    private readonly StreamDeckConnection _deck;
    private readonly IpcClient _tray;
    private readonly KeyFlash _flash;

    public PluginHost(StreamDeckConnection deck, IpcClient tray)
    {
        _deck = deck;
        _tray = tray;
        _flash = new KeyFlash(Moment, Redraw);
    }

    private readonly ConcurrentDictionary<string, Instance> _instances = new();
    private volatile DeviceSnapshot _state = DeviceSnapshot.Disconnected;

    /// <summary>
    /// What the connected model has, or null while the tray has not said. Null offers everything,
    /// which is how the plugin behaved before it was told anything at all.
    /// </summary>
    private volatile DeviceCapabilities? _capabilities;

    public void Start()
    {
        _deck.EventReceived += (_, inbound) => Handle(inbound);
        _tray.SnapshotReceived += (_, snapshot) =>
        {
            _state = snapshot;
            RedrawAll();
        };
        _tray.CapabilitiesReceived += (_, capabilities) =>
        {
            _capabilities = capabilities;
            RedrawAll();
        };
        _tray.ConnectionChanged += (_, connected) =>
        {
            // A dropped link is drawn as no reading at all rather than as the last one, which
            // would otherwise sit there looking current.
            if (!connected) _state = DeviceSnapshot.Disconnected;

            // The tray's hello carries whatever it last knew, which may be from before the
            // earbuds were taken out of the case. Asking on arrival is what makes the deck
            // right immediately rather than at the next thing that happens to change.
            else _tray.Send(IpcCommands.Refresh);

            RedrawAll();
        };
    }

    private void Handle(InboundEvent inbound)
    {
        if (inbound.Context is not { Length: > 0 } context) return;

        switch (inbound.Event)
        {
            case "willAppear":
                _instances[context] = new Instance(
                    inbound.Action ?? "",
                    inbound.Payload?.Settings ?? new ActionSettings(),
                    string.Equals(inbound.Payload?.Controller, "Encoder", StringComparison.Ordinal));
                Redraw(context);
                break;

            case "willDisappear":
                _instances.TryRemove(context, out _);
                _flash.Forget(context);
                break;

            case "didReceiveSettings":
                if (_instances.TryGetValue(context, out var existing))
                {
                    _instances[context] = existing with
                    {
                        Settings = inbound.Payload?.Settings ?? new ActionSettings(),
                    };
                    Redraw(context);
                }
                break;

            case "keyDown":
                Act(context, ticks: 0, pressed: true);
                break;

            case "dialDown":
                Act(context, ticks: 0, pressed: true);
                break;

            case "dialRotate":
                Act(context, inbound.Payload?.Ticks ?? 0, pressed: false);
                break;
        }
    }

    /// <summary>Turns a press or a turn into a command for the tray, if it calls for one.</summary>
    private void Act(string context, int ticks, bool pressed)
    {
        if (!_instances.TryGetValue(context, out var instance)) return;

        if (!_tray.IsConnected)
        {
            _ = _deck.ShowAlertAsync(context);
            return;
        }

        int step = instance.Settings.Step is int configured && configured != 0
            ? configured
            : ActionIds.DefaultStep(instance.ActionId);

        var decision = Decide(instance.ActionId, instance.IsEncoder, pressed, ticks, step, _capabilities);
        if (decision is null) return;

        _tray.Send(decision.Value.Command, decision.Value.Value);

        // The moment outlives the round trip to the tray, so the snapshot that comes back redraws
        // the key with the value the headset actually settled on rather than the one this expected.
        // A dial has its own readout and needs no such answer.
        if (!instance.IsEncoder && ActionIds.Direction(instance.ActionId) != 0) _flash.Show(context);
    }

    /// <summary>
    /// What a given input means, with no connection to anything.
    /// </summary>
    /// <remarks>
    /// A key press carries the sign the user configured, so two keys with opposite steps make an up
    /// and a down. A turn takes its sign from the direction it was turned, so only the size of the
    /// step is used. A dial's press is a button in its own right and never a step, which is why an
    /// encoder press contributes no delta: without that, pressing the volume dial would nudge the
    /// volume, and turning the mute dial would toggle the microphone on every tick.
    ///
    /// An input on a key for something the connected model does not have means nothing, exactly as
    /// a dial press that has no shortcut means nothing. Deciding it here rather than in the caller
    /// is what puts it where it can be checked without a deck and without that model.
    ///
    /// A directed action settles the direction itself, so only the size of the step is its user's:
    /// a down key with a step of -3 still goes down, by three.
    /// </remarks>
    /// <param name="capabilities">
    /// What the model has, or null when nothing has said — which offers everything, as this plugin
    /// behaved before it could ask.
    /// </param>
    internal static (string Command, int Value)? Decide(
        string actionId, bool isEncoder, bool pressed, int ticks, int step,
        DeviceCapabilities? capabilities = null)
    {
        if (!capabilities.Allows(ActionIds.Feature(actionId))) return null;

        int direction = ActionIds.Direction(actionId);
        int size = Math.Abs(step);

        // A directed action's press is its step, on a key and on a dial alike: the direction is the
        // whole reason the action exists, so there is nothing else the press could mean. A turn
        // still follows the way it was turned - a dial that only went one way would not be a dial.
        int delta = direction != 0
            ? (pressed ? direction * size : ticks * size)
            : (pressed ? (isEncoder ? 0 : step) : ticks * size);

        return ActionIds.Subject(actionId) switch
        {
            ActionIds.MicMute => pressed ? (IpcCommands.ToggleMicMute, 0) : null,
            ActionIds.Battery => pressed ? (IpcCommands.Refresh, 0) : null,

            // A dial press is the obvious shortcut for each: centre the balance, mute the
            // microphone. Neither has a counterpart on a plain key, which steps instead - and
            // neither belongs to a directed dial, whose press is already spoken for.
            ActionIds.Balance when direction == 0 && pressed && isEncoder => (IpcCommands.SetBalance, MixCentre),
            ActionIds.MicLevel when direction == 0 && pressed && isEncoder => (IpcCommands.ToggleMicMute, 0),

            ActionIds.Volume when delta != 0 => (IpcCommands.AdjustVolume, delta),
            ActionIds.Balance when delta != 0 => (IpcCommands.AdjustBalance, delta),
            ActionIds.MicLevel when delta != 0 => (IpcCommands.AdjustMicLevel, delta),

            _ => null,
        };
    }

    /// <summary>Centre of the game/chat scale, which runs 0 to 100.</summary>
    private const int MixCentre = 50;

    private void RedrawAll()
    {
        foreach (string context in _instances.Keys) Redraw(context);
    }

    private void Redraw(string context)
    {
        if (!_instances.TryGetValue(context, out var instance)) return;
        var state = _state;
        var capabilities = _capabilities;

        if (instance.IsEncoder)
        {
            _ = _deck.SetFeedbackAsync(context, Feedback(instance.ActionId, state, capabilities));
        }
        else if (ActionIds.Direction(instance.ActionId) == 0)
        {
            _ = _deck.SetImageAsync(context, KeyFace.For(instance.ActionId, state, capabilities));
        }
        else if (_flash.IsShowing(context))
        {
            _ = _deck.SetImageAsync(context, KeyFace.Stepped(instance.ActionId, state, capabilities));
        }
        else
        {
            // A directed key is the picture the manifest gives it, and drawing over that is
            // exactly what the user did not ask for. Clearing rather than never drawing is what
            // gets it back after a press.
            _ = _deck.ClearImageAsync(context);
        }
    }

    /// <summary>What a Stream Deck + dial shows: a name, a reading, and a bar for the travel.</summary>
    /// <remarks>
    /// A dial for something this model does not have reads as nothing, which is the same as a
    /// headset that is not answering — from the dial's point of view there is nothing to show
    /// either way.
    /// </remarks>
    internal static FeedbackPayload Feedback(
        string actionId, DeviceSnapshot state, DeviceCapabilities? capabilities = null)
    {
        if (!state.Connected || !capabilities.Allows(ActionIds.Feature(actionId)))
            return new FeedbackPayload(Title(actionId), "--", new Indicator(0));

        return ActionIds.Subject(actionId) switch
        {
            ActionIds.Volume => new FeedbackPayload(Title(actionId), $"{state.Volume} / {state.VolumeMax}",
                new Indicator(Percentage(state.Volume, state.VolumeMax))),

            ActionIds.Balance => new FeedbackPayload(Title(actionId), KeyFace.Lean(state.Balance),
                new Indicator(state.Balance)),

            ActionIds.MicMute => new FeedbackPayload(Title(actionId), state.MicMuted ? "MUTED" : "LIVE",
                new Indicator(state.MicMuted ? 0 : 100)),

            ActionIds.MicLevel => state.MicLevelAvailable
                ? new FeedbackPayload(Title(actionId), $"{state.MicLevel}%", new Indicator(state.MicLevel))
                : new FeedbackPayload(Title(actionId), "--", new Indicator(0)),

            ActionIds.Battery => new FeedbackPayload(Title(actionId), BatteryLine(state), null),

            _ => new FeedbackPayload(Title(actionId), "--", null),
        };
    }

    private static string BatteryLine(DeviceSnapshot state) => state.Battery.HasSeparateBuds
        ? $"L {Charge(state.Battery.Left)}  R {Charge(state.Battery.Right)}"
        : Charge(state.Battery.Left);

    private static string Charge(int? percent) => percent is int value ? $"{value}%" : "--";

    private static int Percentage(int value, int max) =>
        max <= 0 ? 0 : Math.Clamp(value * 100 / max, 0, 100);

    /// <remarks>
    /// A directed dial is captioned by the action rather than by the setting: two dials for the
    /// same setting sitting side by side, both saying "Volume", would be unreadable. The sign is
    /// the shortest thing that separates them, and a dial's caption has room for little more.
    /// </remarks>
    private static string Title(string actionId) => actionId switch
    {
        ActionIds.Volume => "Volume",
        ActionIds.Balance => "Game / Chat",
        ActionIds.MicMute => "Microphone",
        ActionIds.MicLevel => "Mic level",
        ActionIds.Battery => "Battery",
        ActionIds.VolumeUp => "Volume +",
        ActionIds.VolumeDown => "Volume -",
        ActionIds.MicLevelUp => "Mic level +",
        ActionIds.MicLevelDown => "Mic level -",
        ActionIds.BalanceGame => "More game",
        ActionIds.BalanceChat => "More chat",
        _ => "OpenInzone",
    };

    public void Dispose()
    {
        _flash.Dispose();
        _instances.Clear();
    }
}
