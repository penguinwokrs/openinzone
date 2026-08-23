// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Xml.Linq;

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

    private static XElement DevicePanel()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var xaml = XDocument.Load(Path.Combine(
            directory.FullName, "src", "OpenInzone.Tray", "SettingsWindow.xaml"));

        return xaml.Descendants(Presentation + "StackPanel")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "DevicePanel");
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
}
