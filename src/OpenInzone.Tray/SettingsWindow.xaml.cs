// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using OpenInzone.Control;
using OpenInzone.Ipc;
using OpenInzone.Resources;
using OpenInzone.Settings;

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
    private bool _capturing;
    private bool _duplicate;

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

    /// <summary>
    /// Whether this row is the one currently waiting for a key press. Display-only: capturing
    /// never touches <see cref="Combo"/>, so abandoning it - by starting another row's capture or
    /// closing the window - leaves the row's actual key untouched.
    /// </summary>
    public bool Capturing
    {
        get => _capturing;
        set { _capturing = value; Changed(nameof(Capturing)); Changed(nameof(Display)); }
    }

    /// <summary>
    /// Set when another row holds the same combination. Windows registers the first one and
    /// refuses the second, so this says which rows are in that argument rather than blocking the
    /// assignment - there is no save to block at, and the tray reports the refusal anyway.
    /// </summary>
    public bool Duplicate
    {
        get => _duplicate;
        set { _duplicate = value; Changed(nameof(Duplicate)); Changed(nameof(Display)); Changed(nameof(Brush)); }
    }

    public string Display =>
        Capturing ? Strings.Settings_HotkeyCapturing
        : Conflict ? string.Format(Strings.Settings_HotkeyConflict, Combo)
        : Duplicate ? string.Format(Strings.Settings_HotkeyDuplicate, Combo)
        : Combo.Length == 0 ? Strings.Settings_HotkeyUnassigned
        : Combo;

    public System.Windows.Media.Brush Brush =>
        Conflict || Duplicate ? System.Windows.Media.Brushes.IndianRed : System.Windows.Media.Brushes.White;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class SettingsWindow : Window
{
    private readonly List<HotkeyRow> _rows;
    private readonly HotkeyConfig _config;
    private readonly HotkeyHost _hotkeys;
    private readonly IpcDeviceSurface _headset;
    private HotkeyRow? _capturing;

    /// <summary>
    /// Set while a reading is being copied into the controls, so echoes are not written back. It
    /// starts out set: anything a control raises while the window is still being built is not a
    /// person changing a setting, and there is nothing to write it to yet either.
    /// </summary>
    private bool _showingSettings = true;

    /// <summary>Whether the headset has answered about its settings even once.</summary>
    private bool _settingsArrived;

    /// <summary>
    /// A slider raises a change per pixel of travel. Sending each one would flood the headset, so
    /// writes are coalesced and the last value always goes out - the same arrangement the panel
    /// uses, and for the same reason.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _settingWrites =
        new() { Interval = TimeSpan.FromMilliseconds(150) };

    private readonly Dictionary<string, Action> _pendingSettingWrites = [];

    /// <summary>Every control on the device tab that names a setting, found once as it is built.</summary>
    private readonly List<SettingBinding> _settings = [];

    // NoUpdate until an on-demand check finds one; that transition is what turns the button from
    // Settings_CheckNow into Settings_UpdateButtonInstall and tells a second click to install
    // rather than check again.
    private UpdateInfo _pendingUpdate = UpdateInfo.NoUpdate;
    private bool _updateBusy;

    private string? _downloadedPlugin;
    private bool _pluginBusy;

    /// <summary>Raised whenever a hotkey could not be registered, with the ids that were refused.</summary>
    public event EventHandler<IReadOnlyList<string>>? Rejected;

    public SettingsWindow(HotkeyConfig config, HotkeyHost hotkeys, IpcDeviceSurface headset)
    {
        InitializeComponent();
        _config = config;
        _hotkeys = hotkeys;
        _headset = headset;
        _rows = HotkeyCommand.All
            .Select(c => new HotkeyRow(c, config.Bindings.GetValueOrDefault(c.Id, c.DefaultCombo)))
            .ToList();

        Rows.ItemsSource = _rows;
        MarkDuplicates();

        // What is not registered right now, rather than what was refused when it was last touched:
        // a key taken by something else between sessions would otherwise look like it was working.
        // Two of our own commands sharing a key are refused the same way, and that is a different
        // thing to be told, so those rows keep their own explanation.
        foreach (var row in _rows)
            row.Conflict = !row.Duplicate && hotkeys.Rejected.Contains(row.Id);

        // Assigned before the handlers can fire, so setting the initial state does not write it
        // straight back out.
        AutostartBox.Checked -= OnAutostartChanged;
        AutostartBox.Unchecked -= OnAutostartChanged;
        AutostartBox.IsChecked = Autostart.IsEnabled;
        AutostartBox.Checked += OnAutostartChanged;
        AutostartBox.Unchecked += OnAutostartChanged;

        CheckUpdatesBox.Checked -= OnCheckUpdatesChanged;
        CheckUpdatesBox.Unchecked -= OnCheckUpdatesChanged;
        CheckUpdatesBox.IsChecked = config.CheckForUpdatesAtStartup;
        CheckUpdatesBox.Checked += OnCheckUpdatesChanged;
        CheckUpdatesBox.Unchecked += OnCheckUpdatesChanged;

        // The resolved language, not the configured one: with nothing configured the combo should
        // show what the window is actually in, which is whatever the installer chose.
        string current = UiLanguage.Resolve(_config.Language, AppContext.BaseDirectory);
        UiLanguageBox.SelectedItem = UiLanguageBox.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => (string?)item.Tag == current);

        VersionText.Text = string.Format(Strings.Settings_CurrentVersion, UpdateChecker.CurrentVersion);

        _settingWrites.Tick += (_, _) => FlushSettingWrites();
        _headset.SettingsReceived += OnSettingsReceived;

        // In code rather than in markup, and only now that everything they touch exists. Attached
        // in markup, a handler runs while the window is still being built: a Slider with a
        // Minimum of 1 coerces its value as the markup is read, and OnAmbientLevelChanged then
        // ran against a label that had not been created and a headset that had not been assigned.
        // That crashed the window before it could open, and it reached a release, because neither
        // the tests nor the renderer that draws these tabs runs a handler at all.
        AttachDeviceHandlers();
        _showingSettings = false;

        // Asked for here and asked for again whenever the headset reports anything, until it
        // answers. A request made while the channel is still coming up is simply lost - the daemon
        // may be starting, or restarting - and without this the tab would then say it was asking
        // for the rest of its life.
        _headset.StateChanged += OnHeadsetStateChanged;
        _headset.RequestSettings();
    }

    /// <summary>
    /// Finds every control that names a setting and wires it up.
    /// </summary>
    /// <remarks>
    /// One pass over the markup rather than a handler per control. What each control is for is
    /// declared where it is arranged, so adding a setting is a line of markup and an entry in the
    /// core's catalogue, not an edit in six files.
    /// </remarks>
    private void AttachDeviceHandlers()
    {
        foreach (var site in Sites(DevicePanel))
        {
            var binding = SettingBinding.For(site, Setting.GetId(site)!);
            _settings.Add(binding);
            binding.Attach(WriteSetting);
        }
    }

    /// <summary>
    /// Every element naming a setting. Binding sites do not nest, so the search stops at one
    /// rather than looking inside it — the controls below a site belong to that site.
    /// </summary>
    private static IEnumerable<FrameworkElement> Sites(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is FrameworkElement element && Setting.GetId(element) is not null)
            {
                yield return element;
                continue;
            }

            foreach (var found in Sites(child)) yield return found;
        }
    }

    /// <summary>
    /// What every control on the tab does when it is used: write, and be told what the headset
    /// then says. A slider's writes are coalesced; nothing else needs it.
    /// </summary>
    private void WriteSetting(SettingBinding binding, int value, bool coalesce)
    {
        if (_showingSettings) return;

        if (coalesce) QueueSettingWrite(binding.Id, () => _headset.SetSetting(binding.Id, value));
        else _headset.SetSetting(binding.Id, value);
    }

    // ---- applying -----------------------------------------------------------------------------
    // There is no save button. Every control writes the configuration and takes effect as it is
    // used, which is also why nothing here can be half-applied: a window closed mid-edit has
    // already done everything it was asked to.

    private void OnAutostartChanged(object sender, RoutedEventArgs e) =>
        // Straight to the registry, not through the configuration - see Autostart's class comment
        // for why a second copy there is what caused this to go wrong in the first place.
        Autostart.Set(AutostartBox.IsChecked == true);

    private void OnCheckUpdatesChanged(object sender, RoutedEventArgs e)
    {
        _config.CheckForUpdatesAtStartup = CheckUpdatesBox.IsChecked == true;
        SaveConfig();
    }

    /// <summary>
    /// Saves the choice and offers a restart. The window's text comes from {x:Static}, which is
    /// resolved once when the window is built, so nothing already on screen changes language - and
    /// a window half in the old language is worse than one that plainly asks to restart.
    /// </summary>
    private void OnUiLanguageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;  // Fires once while the window is being built; that is not a choice.
        if (UiLanguageBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;

        string chosen = (string)item.Tag;
        if (chosen == UiLanguage.Resolve(_config.Language, AppContext.BaseDirectory)) return;

        _config.Language = chosen;
        SaveConfig();   // the window's existing save path; the same one CheckUpdatesBox uses

        var answer = System.Windows.MessageBox.Show(this,
            Strings.Settings_RestartPrompt, Strings.Settings_RestartTitle,
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

        if (answer == System.Windows.MessageBoxResult.Yes) RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user accepts a restart. The application owns process lifetime.</summary>
    public event EventHandler? RestartRequested;

    private void SaveConfig()
    {
        try
        {
            _config.Save(HotkeyConfig.DefaultPath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, string.Format(Strings.Settings_SaveFailed, ex.Message),
                "OpenInzone", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Writes the bindings and puts them into effect, reporting anything Windows refused.</summary>
    private void ApplyHotkeys()
    {
        foreach (var row in _rows) _config.Bindings[row.Id] = row.Combo;
        SaveConfig();
        MarkDuplicates();

        var rejected = _hotkeys.Apply(_config);
        if (rejected.Count > 0) Rejected?.Invoke(this, rejected);
    }

    private void MarkDuplicates()
    {
        var counts = _rows.Where(r => r.Combo.Length > 0)
            .GroupBy(r => r.Combo)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        foreach (var row in _rows) row.Duplicate = row.Combo.Length > 0 && counts.Contains(row.Combo);
    }

    // ---- capturing ----------------------------------------------------------------------------

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (_capturing is not null) EndCapture();

        _capturing = _rows.Single(r => r.Id == (string)((System.Windows.Controls.Button)sender).Tag);
        _capturing.Capturing = true;

        // Only for as long as a key is being waited for. Releasing this application's own
        // registrations is what lets CanRegister answer truthfully below; holding them off for the
        // whole time the window is open would mean the hotkeys stopped working while it was.
        _hotkeys.Suspend();
    }

    /// <summary>Puts the hotkeys back the moment a capture ends, whatever ended it.</summary>
    private void EndCapture()
    {
        if (_capturing is null) return;

        _capturing.Capturing = false;
        _capturing = null;
        ApplyHotkeys();
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
            EndCapture();
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
        // Suspend() above released this application's own registrations, so this is answering
        // truthfully: a rejection means some other application holds the combination right now.
        _capturing.Conflict = !HotkeyHost.CanRegister(combo);
        EndCapture();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.Combo = row.DefaultCombo;
            row.Conflict = false;
            row.Capturing = false;
        }

        _capturing = null;
        ApplyHotkeys();
    }

    /// <summary>
    /// A capture left running when the window closes must not leave the hotkeys off. The headset
    /// outlives this window, so what was subscribed to it is let go here too - otherwise every
    /// opening of the settings would leave another dead window listening. A slider let go a moment
    /// before closing has its write flushed rather than dropped.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        EndCapture();
        FlushSettingWrites();
        _headset.SettingsReceived -= OnSettingsReceived;
        _headset.StateChanged -= OnHeadsetStateChanged;
        base.OnClosed(e);
    }

    // ---- the Stream Deck plugin ---------------------------------------------------------------

    /// <summary>
    /// Where the save dialog opens. The folder chosen last time, or Downloads - .NET has no
    /// special folder for that one, so it is composed rather than asked for.
    /// </summary>
    private string PluginFolder => _config.PluginSaveFolder is { Length: > 0 } chosen
                                   && Directory.Exists(chosen)
        ? chosen
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private async void OnPluginDownloadClick(object sender, RoutedEventArgs e)
    {
        if (_pluginBusy) return;

        _pluginBusy = true;
        PluginDownloadButton.IsEnabled = false;
        PluginOpenButton.Visibility = Visibility.Collapsed;
        _downloadedPlugin = null;
        PluginStatusText.Text = Strings.Settings_PluginChecking;

        try
        {
            var asset = await PluginDownloader.FindAsync();
            if (!asset.Found || asset.FileName is not { Length: > 0 } suggestedName)
            {
                // A release with no plugin attached is not an error to hide: the page is still
                // worth offering, and it is the only place an answer could come from.
                PluginStatusText.Text =
                    Strings.Settings_PluginNotFound;
                return;
            }

            // Asked for after the release is read, so the name the dialog offers is the name of
            // the file that is about to arrive rather than a guess at it.
            var save = new Microsoft.Win32.SaveFileDialog
            {
                Title = Strings.Settings_PluginSaveTitle,
                FileName = suggestedName,
                DefaultExt = PluginAsset.Extension,
                AddExtension = true,
                Filter = string.Format(Strings.Settings_PluginFilter, PluginAsset.Extension),
                InitialDirectory = PluginFolder,
                OverwritePrompt = true,
            };

            if (save.ShowDialog(this) != true)
            {
                PluginStatusText.Text = "";
                return;
            }

            // Remembered so the dialog opens where it was left, not so anything is written there
            // without being asked again.
            _config.PluginSaveFolder = Path.GetDirectoryName(save.FileName);
            SaveConfig();

            var progress = new Progress<int>(percent =>
                PluginStatusText.Text = string.Format(Strings.Settings_PluginDownloading, percent));
            string path = await PluginDownloader.SaveAsync(asset, save.FileName, progress);

            _downloadedPlugin = path;
            PluginStatusText.Text = string.Format(Strings.Settings_PluginSaved, path);
            PluginOpenButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            PluginStatusText.Text = string.Format(Strings.Settings_PluginDownloadFailed, ex.Message);
        }
        finally
        {
            PluginDownloadButton.IsEnabled = true;
            _pluginBusy = false;
        }
    }

    /// <summary>
    /// Hands the file to Stream Deck, which is what installing one is: opening a .streamDeckPlugin
    /// is the documented way in, and it puts its own confirmation on screen.
    /// </summary>
    private void OnPluginOpenClick(object sender, RoutedEventArgs e)
    {
        if (_downloadedPlugin is null) return;

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_downloadedPlugin) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PluginStatusText.Text = string.Format(Strings.Settings_PluginOpenFailed, ex.Message);
        }
    }

    private void OnReleasesClick(object sender, RoutedEventArgs e) =>
        ProjectLinks.Open(ProjectLinks.LatestRelease);

    /// <summary>
    /// Opens on the update tab with an update someone else has already found.
    /// </summary>
    /// <remarks>
    /// The state a check made on this window would have left behind, set the same way: the button
    /// installs rather than checks, and the line under it names the version. Nothing is composed
    /// here that <see cref="CheckForUpdateAsync"/> does not compose.
    ///
    /// Three things can go wrong between a notice being raised and being clicked, and this guards
    /// against all of them the same way, by deciding what to keep before ever touching the tab: a
    /// hotkey capture left running would turn the tab keyboard-dead and steal the first key
    /// pressed into whatever row is capturing; a check or an install already in flight owns
    /// <c>_pendingUpdate</c> until it finishes, and <see cref="InstallUpdateAsync"/> reads it again
    /// after the download to verify the digest; and a notice that sat in the notification centre
    /// for days can be older than an update a later check already found, in which case adopting it
    /// would walk the window backwards.
    ///
    /// It does not start the download. Downloading because a notification was clicked would take
    /// the decision away from the person who clicked it.
    /// </remarks>
    public void ShowUpdate(UpdateInfo update)
    {
        // With a capture live, OnPreviewKeyDown marks every key handled at window level, so the
        // tab this is about to select would be keyboard-dead and the first key pressed would be
        // written into whatever row is still waiting. A no-op when nothing is capturing.
        EndCapture();

        if (!_updateBusy && update.Available &&
            (!_pendingUpdate.Available || update.Version > _pendingUpdate.Version))
        {
            _pendingUpdate = update;
            UpdateButton.Content = Strings.Settings_UpdateButtonInstall;
            UpdateStatusText.Text = string.Format(Strings.Settings_UpdateAvailable, update.Version);
        }

        UpdateTab.IsSelected = true;
    }

    /// <summary>
    /// Doubles as "check now" and "install now": the button becomes Settings_UpdateButtonInstall the
    /// moment a check finds something, and a second click on that button installs it rather than
    /// checking again. This is how someone checks on demand when the startup setting is off, so it
    /// works whether or not that checkbox above it is ticked.
    /// </summary>
    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return;

        if (_pendingUpdate.Available) await InstallUpdateAsync();
        else await CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        _updateBusy = true;
        UpdateButton.IsEnabled = false;
        UpdateStatusText.Text = Strings.Settings_UpdateChecking;
        try
        {
            var update = await UpdateChecker.CheckAsync();
            if (update.Available)
            {
                _pendingUpdate = update;
                UpdateButton.Content = Strings.Settings_UpdateButtonInstall;
                UpdateStatusText.Text = string.Format(Strings.Settings_UpdateAvailable, update.Version);
            }
            else
            {
                // Collapsing these into Settings_UpdateUpToDate told someone whose newer release
                // had no installer attached that they were current, which is the one thing this
                // button exists to tell them the truth about.
                UpdateStatusText.Text = update.Reason switch
                {
                    UpdateUnavailableReason.NoInstaller =>
                        Strings.Settings_UpdateNoInstaller,
                    UpdateUnavailableReason.Unreadable =>
                        Strings.Settings_UpdateUnreadable,
                    _ => Strings.Settings_UpdateUpToDate,
                };
            }
        }
        catch (Exception ex)
        {
            // Unlike the silent startup check, someone who pressed this button is owed a reason.
            UpdateStatusText.Text = string.Format(Strings.Settings_UpdateCheckFailed, ex.Message);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
            _updateBusy = false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        _updateBusy = true;
        UpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "";

        // Progress is created on the UI thread, so it captures the WPF dispatcher and the callback
        // below can touch UpdateButton directly without a manual Dispatcher.Invoke.
        var progress = new Progress<int>(percent =>
            UpdateButton.Content = string.Format(Strings.Settings_UpdateDownloading, percent));

        // Before the attempt, not from its return value: a download that throws halfway still
        // leaves a fragment, and this is the only thing that knows where it is.
        string path = UpdateInstaller.CreateStagingPath(_pendingUpdate);
        FileStream? locked = null;

        // Once Run has started the installer, the staged file is a running image, not a fragment -
        // Shutdown throwing afterwards must not fall into the same catch that cleans one up, or it
        // tries to delete a file Windows now refuses to give up.
        bool launched = false;
        try
        {
            // The handle DownloadAsync returns stays open until the installer has been started:
            // verifying a file by name and then launching it by name again would only prove
            // something about the file that was there in between.
            locked = await UpdateInstaller.DownloadAsync(_pendingUpdate, path, progress);

            var digest = UpdateInstaller.VerifyDigest(locked, _pendingUpdate.Sha256);
            if (digest == UpdateInstaller.DigestResult.Mismatch)
            {
                // This downloads an executable and runs it - a digest that does not match is a
                // reason to stop, not a reason to ask.
                UpdateStatusText.Text = Strings.Settings_UpdateVerifyFailed;
                Abandon(path, ref locked);
                return;
            }

            if (digest == UpdateInstaller.DigestResult.Absent)
            {
                // No digest to check against is not the same as a failed check - it is the user's
                // call, and it defaults to No: a file fetched by HttpClient carries no
                // Mark-of-the-Web, so SmartScreen will not weigh in as a second opinion the way it
                // would on a browser download. The digest and this prompt are the only two.
                var choice = System.Windows.MessageBox.Show(this,
                    Strings.Settings_UpdateNoDigest,
                    "OpenInzone", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes)
                {
                    UpdateStatusText.Text = Strings.Settings_UpdateCancelled;
                    Abandon(path, ref locked);
                    return;
                }
            }

            // The installer stops this process itself, but that races the window still being open;
            // exiting here is what actually lets it replace the running copy.
            UpdateInstaller.Run(path);
            launched = true;
            locked?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            // Verification and Process.Start throw as readily as the download does, and a failure
            // that left the button disabled with no message was indistinguishable from a hang.
            UpdateStatusText.Text = string.Format(Strings.Settings_UpdateFailed, ex.Message);

            // Only Shutdown can throw once launched is true - the handle is already closed and the
            // installer is already the one running that file, so there is nothing left to abandon.
            if (!launched) Abandon(path, ref locked);
        }
    }

    /// <summary>
    /// Gives up on a downloaded installer: the handle first, because the file cannot be deleted
    /// while it is held, then the file and the directory it was staged in, then the button.
    /// </summary>
    private void Abandon(string path, ref FileStream? locked)
    {
        locked?.Dispose();
        locked = null;
        UpdateInstaller.CleanUp(path);
        FinishBusyWithUpdateStillAvailable();
    }

    private void FinishBusyWithUpdateStillAvailable()
    {
        UpdateButton.Content = Strings.Settings_UpdateButtonInstall;
        UpdateButton.IsEnabled = true;
        _updateBusy = false;
    }

    // ---- the settings INZONE Hub also offers --------------------------------------------------
    // Every control writes as it is used and is then told what the headset says, which is what
    // arrives here. Nothing is composed locally: a mode change comes back with the level the
    // headset kept, and a failed write comes back as the value that is still in force.

    private void OnSettingsReceived(object? sender, IReadOnlyList<SettingValue> settings)
    {
        _settingsArrived = true;
        Dispatcher.BeginInvoke(() => ShowSettings(settings));
    }

    /// <summary>
    /// Raised on a background thread, on connect and on every change the headset reports. Nothing
    /// here needs the state itself: it is the news that the channel is up, and therefore that a
    /// request that went nowhere is worth making again.
    /// </summary>
    private void OnHeadsetStateChanged(object? sender, DeviceSnapshot state)
    {
        if (_settingsArrived) return;

        if (state.Connected) _headset.RequestSettings();
        else Dispatcher.BeginInvoke(() => ShowSettings([]));
    }

    /// <summary>
    /// Draws what the headset now says. A setting this model does not have is not in the list at
    /// all, and its control is hidden rather than shown doing nothing — which is the same rule as
    /// before, now answered by the headset's own capability map instead of by a timeout.
    /// </summary>
    private void ShowSettings(IReadOnlyList<SettingValue> settings)
    {
        _showingSettings = true;
        try
        {
            bool anything = settings.Count > 0;
            DevicePanel.IsEnabled = anything;
            DeviceStatusText.Text = anything
                ? Strings.Settings_DeviceApplied
                : Strings.Settings_DeviceUnresponsive;

            foreach (var binding in _settings) binding.Show(settings.Value);

            // The one thing no catalogue entry can say: these two belong to ambient sound. The
            // headset keeps them in every mode, but showing them as adjustable while they do
            // nothing would be a lie. Both keep the value the headset holds while they are greyed,
            // so switching back to ambient sound has no surprises in it.
            bool ambient = settings.Value(SettingCatalogue.AmbientMode) == SettingCatalogue.AmbientSoundMode;
            AmbientLevelRow.IsEnabled = ambient;
            VoiceFocusBox.IsEnabled = ambient;
        }
        finally
        {
            _showingSettings = false;
        }
    }

    /// <summary>Replaces that control's pending write; one slider never cancels another's.</summary>
    private void QueueSettingWrite(string control, Action write)
    {
        if (_showingSettings) return;

        _pendingSettingWrites[control] = write;
        _settingWrites.Stop();
        _settingWrites.Start();
    }

    private void FlushSettingWrites()
    {
        _settingWrites.Stop();
        foreach (var write in _pendingSettingWrites.Values) write();
        _pendingSettingWrites.Clear();
    }

}
