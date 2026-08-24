// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Xml.Linq;
using OpenInzone.Settings;

namespace OpenInzone.Tests.Control;

/// <summary>
/// A handler attached in markup runs while the window is still being built, before the constructor
/// has assigned anything. The device tab found this the hard way: a Slider with a Minimum of 1
/// coerces its value as the markup is read, and the handler that answered ran against a label that
/// did not exist yet. The window could not open at all, and it shipped, because nothing that runs
/// in a build ever runs one of these handlers - the tests do not construct the window, and the
/// renderer that draws the tabs strips the handlers out before parsing.
///
/// So the rule is checked instead: the device tab attaches its handlers in code, once everything
/// they touch exists.
/// </summary>
public class SettingsMarkupTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement DevicePanel() => SettingsXaml()
        .Descendants(Presentation + "StackPanel")
        .Single(element => (string?)element.Attribute(Xaml + "Name") == "DevicePanel");

    /// <summary>The settings window's markup, read off disk rather than from a built assembly.</summary>
    private static XDocument SettingsXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(
            directory.FullName, "src", "OpenInzone.Tray", "SettingsWindow.xaml"));
    }

    /// <summary>
    /// Everything WPF will call while reading the markup. Bindings are not included: they are
    /// evaluated, not invoked, and a template's IsChecked binding is not an event handler.
    /// </summary>
    private static readonly string[] EventAttributes =
        ["Click", "Checked", "Unchecked", "ValueChanged", "SelectionChanged", "TextChanged", "Loaded"];

    [Fact]
    public void The_device_tab_attaches_no_handlers_in_markup()
    {
        var attached = DevicePanel()
            .DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Where(attribute => EventAttributes.Contains(attribute.Name.LocalName))
            .Select(attribute => $"{attribute.Parent!.Name.LocalName}.{attribute.Name.LocalName}");

        Assert.Empty(attached);
    }

    private static readonly XNamespace Tray = "clr-namespace:OpenInzone.Tray";

    /// <summary>
    /// Every setting named anywhere on the tab. A heading over several of them names them all,
    /// separated by spaces, which is what lets it go away with the last of them.
    /// </summary>
    private static IEnumerable<string> NamedSettings() => DevicePanel()
        .DescendantsAndSelf()
        .Select(element => (string?)element.Attribute(Tray + "Setting.Id"))
        .OfType<string>()
        .SelectMany(ids => ids.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The device tab is drawn by one binder that walks the markup looking for controls which name
    /// the setting they are for. A name the catalogue does not have binds to nothing at all, and
    /// nothing at runtime would say so — the control would simply sit there, filled by no one and
    /// writing nowhere. So the markup is read here and checked against the catalogue.
    /// </summary>
    [Fact]
    public void Every_control_that_names_a_setting_names_one_the_catalogue_has()
    {
        var named = NamedSettings().ToList();

        Assert.NotEmpty(named);
        Assert.All(named, id => Assert.NotNull(SettingCatalogue.ById(id)));
    }

    /// <summary>
    /// And the other way round: a setting the catalogue describes but nothing in the markup names
    /// is one the headset answers for and nobody is ever shown.
    /// </summary>
    [Fact]
    public void Every_setting_the_catalogue_describes_is_somewhere_on_the_tab()
    {
        var named = NamedSettings().ToHashSet();

        Assert.All(SettingCatalogue.All, setting => Assert.Contains(setting.Id, named));
    }

    /// <summary>
    /// The ambient level and voice focus are only worth offering while ambient sound is the chosen
    /// mode, and the window decides that by comparing the reported mode against
    /// <see cref="SettingCatalogue.AmbientSoundMode"/>. The number it compares against has to be
    /// the one this radio writes when someone picks it - the markup's Tag - or the window greys out
    /// the wrong things while every test still passes.
    /// </summary>
    [Fact]
    public void The_ambient_radio_writes_the_mode_the_catalogue_names()
    {
        var radio = DevicePanel().Descendants(Presentation + "RadioButton")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "AmbientButton");

        Assert.Equal(SettingCatalogue.AmbientSoundMode, int.Parse((string)radio.Attribute("Tag")!));
    }

    /// <summary>
    /// Clicking the notice that an update is available opens this window on the update tab, which
    /// the code does by name. Losing the name alone would not get past a build - <c>ShowUpdate</c>
    /// refers to <c>UpdateTab</c> directly, so the field the name generates has to still exist. What
    /// this catches is someone removing the name and every line that uses it together: a build that
    /// still succeeds, with the notice landing on whatever tab happens to be first instead.
    /// </summary>
    [Fact]
    public void The_update_tab_carries_the_name_the_code_selects_it_by()
    {
        var named = SettingsXaml()
            .Descendants(Presentation + "TabItem")
            .Select(element => (string?)element.Attribute(Xaml + "Name"))
            .OfType<string>();

        Assert.Contains("UpdateTab", named);
    }
}
