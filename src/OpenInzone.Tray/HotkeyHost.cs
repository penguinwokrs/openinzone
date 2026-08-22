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

    /// <summary>Applies a configuration, returning the ids of commands whose key was already taken.</summary>
    public IReadOnlyList<string> Apply(HotkeyConfig config)
    {
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

        return rejected;
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
            command.Run(_actions);
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
