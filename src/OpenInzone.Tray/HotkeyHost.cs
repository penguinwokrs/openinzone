// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows.Interop;
using OpenInzone.Control;
using OpenInzone.Tray.Native;

namespace OpenInzone.Tray;

/// <summary>
/// Registers the bound combinations against a message-only window and runs the matching command
/// when one fires. RegisterHotKey is first come, first served: a combination another application
/// already holds is reported rather than thrown, so one conflict does not cost every other binding.
/// </summary>
public sealed class HotkeyHost : IDisposable
{
    private readonly IDeviceActions _actions;
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyCommand> _registered = [];
    private int _nextId = 1;
    private HotkeyConfig? _appliedConfig;
    private bool _suspended;

    public HotkeyHost(IDeviceActions actions)
    {
        _actions = actions;
        _source = new HwndSource(new HwndSourceParameters("OpenInzone hotkeys")
        {
            // A message-only window: never shown, never in the taskbar.
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
        });
        _source.AddHook(OnMessage);
    }

    /// <summary>
    /// The commands whose key could not be taken the last time this applied one. Kept because a
    /// balloon at startup is gone by the time anyone opens the settings, and a binding that is not
    /// registered otherwise sits there looking exactly like one that is.
    /// </summary>
    public IReadOnlyList<string> Rejected { get; private set; } = [];

    /// <summary>Applies a configuration, returning the ids of commands whose key was already taken.</summary>
    public IReadOnlyList<string> Apply(HotkeyConfig config)
    {
        // Remembered so Resume can put this same configuration back after a Suspend.
        _appliedConfig = config;
        _suspended = false;
        Unregister();

        var rejected = new List<string>();
        foreach (var command in HotkeyCommand.All)
        {
            if (!config.Bindings.TryGetValue(command.Id, out var text) || string.IsNullOrWhiteSpace(text))
                continue;
            if (!KeyCombo.TryParse(text, out var combo))
            {
                rejected.Add(command.Id);
                continue;
            }

            int id = _nextId++;
            if (NativeMethods.RegisterHotKey(_source.Handle, id, combo.Modifiers, combo.VirtualKey))
                _registered[id] = command;
            else
                rejected.Add(command.Id);
        }

        Rejected = rejected;
        return rejected;
    }

    /// <summary>
    /// Releases this host's own registrations while the user is choosing keys elsewhere. RegisterHotKey
    /// refuses a combination already held by any window, including one of ours, so a key this host has
    /// bound would otherwise always probe as taken even when the user is simply re-confirming it or
    /// moving it to another command. Letting go of it here is what makes <see cref="CanRegister"/>
    /// honest for the duration.
    /// </summary>
    public void Suspend()
    {
        if (_suspended) return;
        Unregister();
        _suspended = true;
    }

    /// <summary>
    /// Re-registers the configuration last given to <see cref="Apply"/>, undoing a Suspend. A call
    /// with nothing suspended makes no sense - Apply already holds the registrations, so redoing
    /// the unregister-and-register cycle would only repeat whatever rejections it just reported -
    /// so this is a no-op in that case.
    /// </summary>
    public IReadOnlyList<string> Resume()
    {
        if (_appliedConfig is null)
        {
            // Nothing has been applied yet, so there is nothing to re-register - but Apply is what
            // normally clears _suspended, and it is not being called here, so it must be cleared
            // directly or a later Suspend would find it already (and wrongly) set.
            _suspended = false;
            return [];
        }

        if (!_suspended) return [];

        return Apply(_appliedConfig);
    }

    /// <summary>
    /// Tests a combination by taking it and letting it go again, which is the only way to know: the
    /// answer is whatever RegisterHotKey says at this moment.
    /// </summary>
    public static bool CanRegister(KeyCombo combo)
    {
        using var probe = new HwndSource(new HwndSourceParameters("OpenInzone hotkey probe")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
        });

        if (!NativeMethods.RegisterHotKey(probe.Handle, 1, combo.Modifiers, combo.VirtualKey)) return false;
        NativeMethods.UnregisterHotKey(probe.Handle, 1);
        return true;
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _registered.TryGetValue((int)wParam, out var command))
        {
            // Same principle as a lost RegisterHotKey race: one command should not be able to take
            // the rest down with it. This callback runs from the native window procedure, where an
            // escaping exception fails the process rather than reaching any managed handler.
            try { command.Run(_actions); }
            catch { /* reported nowhere; there is no application-level handler this can safely reach */ }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Unregister()
    {
        foreach (var id in _registered.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(OnMessage);
        _source.Dispose();
    }
}
