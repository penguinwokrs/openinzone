// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;
using System.Windows.Controls;

// The project's implicit usings bring in Windows Forms for the notification-area icon, and it
// names half of these too. These are the WPF ones.
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using RadioButton = System.Windows.Controls.RadioButton;

namespace OpenInzone.Tray;

/// <summary>
/// One setting on the device tab, and the controls the markup gave it.
/// </summary>
/// <remarks>
/// The tab used to have a handler and a fill for every control, written out one setting at a time.
/// This is the same work said once: whatever kind of control sits under a
/// <see cref="Setting.IdProperty"/> is shown, filled and written the way that kind of control is.
///
/// Values are plain integers, as they are on the channel and in the core's catalogue — 0 or 1 for
/// a toggle, and the headset's own number for anything else. Nothing here knows what any
/// particular setting means, which is the point.
/// </remarks>
internal sealed class SettingBinding
{
    private readonly FrameworkElement _site;
    private readonly Slider? _slider;
    private readonly TextBlock? _valueText;
    private readonly CheckBox? _box;
    private readonly ComboBox? _combo;
    private readonly IReadOnlyList<RadioButton> _radios;

    public string Id { get; }

    private SettingBinding(FrameworkElement site, string id)
    {
        _site = site;
        Id = id;

        var parts = Parts(site).ToList();
        _slider = parts.OfType<Slider>().FirstOrDefault();
        _valueText = parts.OfType<TextBlock>().FirstOrDefault(Setting.GetShowsValue);
        _box = parts.OfType<CheckBox>().FirstOrDefault();
        _combo = parts.OfType<ComboBox>().FirstOrDefault();
        _radios = parts.OfType<RadioButton>().ToList();
    }

    public static SettingBinding For(FrameworkElement site, string id) => new(site, id);

    /// <summary>
    /// An element and everything below it in the logical tree. The logical tree rather than the
    /// visual one: this runs while the window is still being built, and nothing has been rendered.
    /// </summary>
    public static IEnumerable<DependencyObject> Parts(DependencyObject root)
    {
        yield return root;

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var part in Parts(child))
                yield return part;
    }

    /// <summary>
    /// Shows what the headset says, or hides the control when this model has no such setting.
    /// Hidden rather than disabled: a control that does nothing is worse than no control.
    /// </summary>
    public void Show(int? value)
    {
        _site.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        if (value is not int number) return;

        if (_slider is not null) _slider.Value = number;
        if (_valueText is not null) _valueText.Text = number.ToString();
        if (_box is not null) _box.IsChecked = number == 1;

        // Picked by the Tag the markup gave, rather than by position. The two agree only while the
        // list happens to be written in value order, and a list reordered for reading - or a value
        // this build does not know - would otherwise silently select the wrong one.
        if (_combo is not null)
            _combo.SelectedItem = _combo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Value(item) == number);

        foreach (var radio in _radios) radio.IsChecked = Value(radio) == number;
    }

    /// <summary>
    /// Wires every control to write as it is used. There is no save button anywhere in this window.
    /// </summary>
    /// <param name="write">
    /// Takes the binding, the value, and whether the writes may be coalesced — which only a slider
    /// needs, because it raises a change per pixel of travel.
    /// </param>
    public void Attach(Action<SettingBinding, int, bool> write)
    {
        if (_slider is not null)
            _slider.ValueChanged += (_, e) =>
            {
                if (_valueText is not null) _valueText.Text = ((int)e.NewValue).ToString();
                write(this, (int)e.NewValue, true);
            };

        if (_box is not null)
        {
            void Toggled(object sender, RoutedEventArgs e) =>
                write(this, _box.IsChecked == true ? 1 : 0, false);

            _box.Checked += Toggled;
            _box.Unchecked += Toggled;
        }

        if (_combo is not null)
            _combo.SelectionChanged += (_, _) =>
            {
                if (_combo.SelectedItem is ComboBoxItem item) write(this, Value(item), false);
            };

        foreach (var radio in _radios)
            radio.Checked += (sender, _) => write(this, Value((FrameworkElement)sender), false);
    }

    /// <summary>The value the markup gave a choice, which is the byte the headset uses for it.</summary>
    private static int Value(FrameworkElement element) =>
        element.Tag is string tag && int.TryParse(tag, out int value) ? value : -1;
}
