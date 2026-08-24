// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;

namespace OpenInzone.Tray;

/// <summary>
/// Says, in the markup, which setting a control is for.
/// </summary>
/// <remarks>
/// The device tab used to be a handler per control and a line per control in the code that filled
/// it, which is three places to keep in step for one setting. Now the layout — which is something
/// a person arranged, and stays that way — names what each part of it drives, and one binder in
/// <see cref="SettingsWindow"/> shows, fills and writes every one of them.
///
/// The name is the id the setting travels under on the channel and in the core's catalogue.
/// A test reads this markup and refuses an id the catalogue does not have.
/// </remarks>
public static class Setting
{
    /// <summary>
    /// The setting this element and everything inside it is for. Binding sites do not nest: the
    /// binder takes every control below one as belonging to that setting.
    /// </summary>
    /// <remarks>
    /// More than one id, separated by spaces, means "while any of these is here" — which is what a
    /// heading over several settings needs, and the only thing it needs. A control still drives the
    /// first id named, so anything with more than one is a label rather than a control.
    /// </remarks>
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(string), typeof(Setting), new PropertyMetadata(null));

    public static string? GetId(DependencyObject element) => (string?)element.GetValue(IdProperty);

    public static void SetId(DependencyObject element, string? value) => element.SetValue(IdProperty, value);

    /// <summary>Marks the label that shows a slider's number, so the binder can keep it in step.</summary>
    public static readonly DependencyProperty ShowsValueProperty = DependencyProperty.RegisterAttached(
        "ShowsValue", typeof(bool), typeof(Setting), new PropertyMetadata(false));

    public static bool GetShowsValue(DependencyObject element) => (bool)element.GetValue(ShowsValueProperty);

    public static void SetShowsValue(DependencyObject element, bool value) =>
        element.SetValue(ShowsValueProperty, value);
}
