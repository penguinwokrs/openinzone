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
internal sealed class PluginHost(StreamDeckConnection deck, IpcClient tray) : IDisposable
{
    /// <summary>One key or dial the user has placed on a deck.</summary>
    private sealed record Instance(string ActionId, ActionSettings Settings, bool IsEncoder);

    private readonly ConcurrentDictionary<string, Instance> _instances = new();
    private volatile DeviceSnapshot _state = DeviceSnapshot.Disconnected;

    /// <summary>
    /// What the connected model has, or null while the tray has not said. Null offers everything,
    /// which is how the plugin behaved before it was told anything at all.
    /// </summary>
    private volatile DeviceCapabilities? _capabilities;

    public void Start()
    {
        deck.EventReceived += (_, inbound) => Handle(inbound);
        tray.SnapshotReceived += (_, snapshot) =>
        {
            _state = snapshot;
            RedrawAll();
        };
        tray.CapabilitiesReceived += (_, capabilities) =>
        {
            _capabilities = capabilities;
            RedrawAll();
        };
        tray.ConnectionChanged += (_, connected) =>
        {
            // A dropped link is drawn as no reading at all rather than as the last one, which
            // would otherwise sit there looking current.
            if (!connected) _state = DeviceSnapshot.Disconnected;

            // The tray's hello carries whatever it last knew, which may be from before the
            // earbuds were taken out of the case. Asking on arrival is what makes the deck
            // right immediately rather than at the next thing that happens to change.
            else tray.Send(IpcCommands.Refresh);

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

        if (!tray.IsConnected || !Offers(instance.ActionId))
        {
            _ = deck.ShowAlertAsync(context);
            return;
        }

        int step = instance.Settings.Step is int configured && configured != 0
            ? configured
            : ActionIds.DefaultStep(instance.ActionId);

        var decision = Decide(instance.ActionId, instance.IsEncoder, pressed, ticks, step);
        if (decision is not null) tray.Send(decision.Value.Command, decision.Value.Value);
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
    /// </remarks>
    internal static (string Command, int Value)? Decide(
        string actionId, bool isEncoder, bool pressed, int ticks, int step)
    {
        int delta = pressed ? (isEncoder ? 0 : step) : ticks * Math.Abs(step);

        return actionId switch
        {
            ActionIds.MicMute => pressed ? (IpcCommands.ToggleMicMute, 0) : null,
            ActionIds.Battery => pressed ? (IpcCommands.Refresh, 0) : null,

            // A dial press is the obvious shortcut for each: centre the balance, mute the
            // microphone. Neither has a counterpart on a plain key, which steps instead.
            ActionIds.Balance when pressed && isEncoder => (IpcCommands.SetBalance, MixCentre),
            ActionIds.MicLevel when pressed && isEncoder => (IpcCommands.ToggleMicMute, 0),

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

    /// <summary>Whether this model has what a key is for.</summary>
    private bool Offers(string actionId) => _capabilities.Allows(ActionIds.Feature(actionId));

    private void Redraw(string context)
    {
        if (!_instances.TryGetValue(context, out var instance)) return;

        // A key for something this model does not have is drawn as no reading. It is the same face
        // as a headset that is not answering, which is the truth from the key's point of view:
        // there is nothing there to show.
        var state = Offers(instance.ActionId) ? _state : DeviceSnapshot.Disconnected;

        if (instance.IsEncoder) _ = deck.SetFeedbackAsync(context, Feedback(instance.ActionId, state));
        else _ = deck.SetImageAsync(context, KeyFace.For(instance.ActionId, state));
    }

    /// <summary>What a Stream Deck + dial shows: a name, a reading, and a bar for the travel.</summary>
    internal static FeedbackPayload Feedback(string actionId, DeviceSnapshot state)
    {
        if (!state.Connected)
            return new FeedbackPayload(Title(actionId), "--", new Indicator(0));

        return actionId switch
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

    private static string Title(string actionId) => actionId switch
    {
        ActionIds.Volume => "Volume",
        ActionIds.Balance => "Game / Chat",
        ActionIds.MicMute => "Microphone",
        ActionIds.MicLevel => "Mic level",
        ActionIds.Battery => "Battery",
        _ => "OpenInzone",
    };

    public void Dispose() => _instances.Clear();
}
