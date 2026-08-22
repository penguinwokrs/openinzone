// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using OpenInzone.Control;

namespace OpenInzone.Tray;

/// <summary>
/// One row of the table. Public because WPF's binding engine cannot reach the properties of a
/// non-public type, and it notifies so that capturing a key updates the button without rebuilding
/// the list.
/// </summary>
public sealed class HotkeyRow(HotkeyCommand command, string combo) : INotifyPropertyChanged
{
    private string _combo = combo;
    private bool _conflict;

    public string Id => command.Id;
    public string DisplayName => command.DisplayName;
    public string DefaultCombo => command.DefaultCombo;

    public string Combo
    {
        get => _combo;
        set { _combo = value; Changed(nameof(Combo)); Changed(nameof(Display)); }
    }

    public bool Conflict
    {
        get => _conflict;
        set { _conflict = value; Changed(nameof(Conflict)); Changed(nameof(Display)); Changed(nameof(Brush)); }
    }

    public string Display => Conflict ? $"{Combo}（使用中）" : Combo.Length == 0 ? "未割り当て" : Combo;
    public System.Windows.Media.Brush Brush =>
        Conflict ? System.Windows.Media.Brushes.IndianRed : System.Windows.Media.Brushes.White;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class SettingsWindow : Window
{
    private readonly List<HotkeyRow> _rows;
    private readonly HotkeyConfig _config;
    private HotkeyRow? _capturing;

    /// <summary>Raised when the user saves. The argument is the configuration already written to disk.</summary>
    public event EventHandler<HotkeyConfig>? Saved;

    public SettingsWindow(HotkeyConfig config)
    {
        InitializeComponent();
        _config = config;
        _rows = HotkeyCommand.All
            .Select(c => new HotkeyRow(c, config.Bindings.GetValueOrDefault(c.Id, c.DefaultCombo)))
            .ToList();

        Rows.ItemsSource = _rows;
        AutostartBox.IsChecked = Autostart.IsEnabled;
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        _capturing = _rows.Single(r => r.Id == (string)((System.Windows.Controls.Button)sender).Tag);
        _capturing.Combo = "キーを押してください";
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (_capturing is null) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;                                          // still waiting for the real key

        if (key == Key.Escape)
        {
            _capturing.Combo = "";
            _capturing.Conflict = false;
            _capturing = null;
            return;
        }

        uint modifiers = 0;
        var down = Keyboard.Modifiers;
        if (down.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (down.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (down.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (down.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        KeyCombo combo;
        try
        {
            combo = KeyCombo.FromKey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        }
        catch (FormatException)
        {
            return;                                          // a key with no name; keep waiting
        }

        _capturing.Combo = combo.Text;
        // HotkeyHost.Suspend() (called around this window's lifetime by App) releases this
        // application's own registrations, so CanRegister here is answering truthfully: a
        // rejection means some other application holds the combination right now.
        _capturing.Conflict = !HotkeyHost.CanRegister(combo);
        _capturing = null;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.Combo = row.DefaultCombo;
            row.Conflict = false;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var duplicate = _rows.Where(r => r.Combo.Length > 0)
            .GroupBy(r => r.Combo).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            System.Windows.MessageBox.Show(this, $"{duplicate.Key} が複数のコマンドに割り当てられています。",
                "OpenInzone", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var row in _rows) _config.Bindings[row.Id] = row.Combo;
        _config.Autostart = AutostartBox.IsChecked == true;
        _config.Save(HotkeyConfig.DefaultPath);
        Autostart.Set(_config.Autostart);

        Saved?.Invoke(this, _config);
        Close();
    }
}
