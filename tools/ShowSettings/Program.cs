// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenInzone.Control;
using OpenInzone.Tray;
using Application = System.Windows.Application;
using Window = System.Windows.Window;

namespace OpenInzone.ShowSettings;

/// <summary>
/// Builds the real settings window the way the tray does, against whatever daemon is running, and
/// saves a picture of each tab.
/// </summary>
/// <remarks>
/// Written after a settings window that could not open at all reached a release. A handler
/// attached in markup runs while the window is still being built - the ambient slider's Minimum
/// coerced its value as the markup was read - and the handler reached for a label that did not
/// exist yet. Nothing caught it: the tests do not construct the window, and
/// assets/make-settings-screenshot.ps1 strips the handlers out before parsing, because it has no
/// code-behind to resolve them against. Between them they cover the markup and the logic and leave
/// out the one thing that failed, which is the two being put together.
///
/// So this constructs it for real. It also fills from a real headset over the real channel, which
/// is the only way to see that what a person is shown is what the device actually said.
///
/// It takes no hotkeys - an empty configuration - so that running it cannot take a key away from a
/// tray that is already running.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool lateStart = args.Contains("--late-start");
        args = [.. args.Where(argument => argument != "--late-start")];

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("usage: show-settings <output-directory> [seconds-to-wait] [--late-start]");
            Console.WriteLine();
            Console.WriteLine("  --late-start  open the window before the channel is up, as happens when");
            Console.WriteLine("                the daemon is starting. The window's first request for the");
            Console.WriteLine("                settings is lost, and it has to ask again.");
            return args.Length == 0 ? 2 : 0;
        }

        string directory = args[0];
        double seconds = args.Length > 1 && double.TryParse(args[1], out double given) ? given : 6;
        Directory.CreateDirectory(directory);

        var application = new Application();
        try
        {
            var config = new HotkeyConfig();
            using var headset = new IpcDeviceSurface();
            using var hotkeys = new HotkeyHost(headset);

            // Started before the window is built, as the tray starts it at login: the window asks
            // the headset for its settings as it opens. --late-start does it the other way round,
            // which is what happens when the daemon is not there yet.
            if (!lateStart) headset.Start();

            var window = new SettingsWindow(config, hotkeys, headset);
            window.Show();

            if (lateStart) headset.Start();

            // The window asks for the settings as it opens; this is the answer coming back over
            // the pipe, which is not something that can be waited on from here.
            Pump(application, TimeSpan.FromSeconds(seconds));

            application.Dispatcher.Invoke(() =>
            {
                var tabs = (TabControl)window.Content;
                for (int index = 0; index < tabs.Items.Count; index++)
                {
                    tabs.SelectedIndex = index;
                    window.UpdateLayout();
                    string name = ((TabItem)tabs.Items[index]).Header?.ToString() ?? $"tab-{index}";
                    Save(window, Path.Combine(directory, $"{index}-{name}.png"));
                }

                Report(window);
                window.Close();
            });
        }
        catch (Exception ex)
        {
            // The point of this tool: a window that cannot be built says so here, with the stack
            // that says where, rather than in front of somebody who has just installed it.
            Console.Error.WriteLine($"the settings window could not be opened: {ex}");
            return 1;
        }
        finally
        {
            application.Shutdown();
        }

        return 0;
    }

    /// <summary>Keeps the dispatcher running without a message loop of its own to stop.</summary>
    private static void Pump(Application application, TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            application.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(50);
        }
    }

    /// <summary>What the device tab ended up showing, in text, so a run can be read in a log.</summary>
    private static void Report(Window window)
    {
        Console.WriteLine(Text(window, "DeviceStatusText"));

        foreach (var (label, name) in new[]
        {
            ("ambient level", "AmbientLevelText"),
            ("sidetone", "SidetoneText"),
        })
        {
            Console.WriteLine($"{label}: {Text(window, name)}");
        }

        foreach (var (label, name) in new[]
        {
            ("ambient off", "AmbientOffButton"),
            ("noise cancelling", "NoiseCancellingButton"),
            ("ambient sound", "AmbientButton"),
        })
        {
            if (window.FindName(name) is RadioButton button && button.IsChecked == true)
                Console.WriteLine($"ambient mode: {label}");
        }

        foreach (var (label, name) in new[]
        {
            ("voice focus", "VoiceFocusBox"),
            ("auto power off", "AutoPowerOffBox"),
            ("bluetooth switching", "BluetoothAutoSwitchBox"),
            ("voice guidance", "VoiceGuidanceBox"),
        })
        {
            if (window.FindName(name) is CheckBox box)
                Console.WriteLine($"{label}: {(box.Visibility == System.Windows.Visibility.Visible ? box.IsChecked == true ? "on" : "off" : "not offered")}");
        }

        if (window.FindName("LanguageBox") is ComboBox language && language.SelectedItem is ComboBoxItem item)
            Console.WriteLine($"language: {item.Content} ({item.Tag})");
    }

    private static string Text(Window window, string name) =>
        window.FindName(name) is TextBlock block ? block.Text : "";

    private static void Save(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap(
            (int)(window.ActualWidth * 2), (int)(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
