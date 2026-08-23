// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.ComponentModel;
using System.IO;
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
        Capturing ? "キーを押してください"
        : Conflict ? $"{Combo}（他のアプリが使用中）"
        : Duplicate ? $"{Combo}（重複）"
        : Combo.Length == 0 ? "未割り当て"
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
    private HotkeyRow? _capturing;

    // NoUpdate until an on-demand check finds one; that transition is what turns the button from
    // "確認" into "更新" and tells a second click to install rather than check again.
    private UpdateInfo _pendingUpdate = UpdateInfo.NoUpdate;
    private bool _updateBusy;

    private string? _downloadedPlugin;
    private bool _pluginBusy;

    /// <summary>Raised whenever a hotkey could not be registered, with the ids that were refused.</summary>
    public event EventHandler<IReadOnlyList<string>>? Rejected;

    public SettingsWindow(HotkeyConfig config, HotkeyHost hotkeys)
    {
        InitializeComponent();
        _config = config;
        _hotkeys = hotkeys;
        _rows = HotkeyCommand.All
            .Select(c => new HotkeyRow(c, config.Bindings.GetValueOrDefault(c.Id, c.DefaultCombo)))
            .ToList();

        Rows.ItemsSource = _rows;
        MarkDuplicates();

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

        VersionText.Text = $"現在のバージョン: {UpdateChecker.CurrentVersion}";
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

    private void SaveConfig()
    {
        try
        {
            _config.Save(HotkeyConfig.DefaultPath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"設定を保存できませんでした: {ex.Message}",
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

    /// <summary>A capture left running when the window closes must not leave the hotkeys off.</summary>
    protected override void OnClosed(EventArgs e)
    {
        EndCapture();
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
        PluginStatusText.Text = "リリースを確認しています…";

        try
        {
            var asset = await PluginDownloader.FindAsync();
            if (!asset.Found || asset.FileName is not { Length: > 0 } suggestedName)
            {
                // A release with no plugin attached is not an error to hide: the page is still
                // worth offering, and it is the only place an answer could come from.
                PluginStatusText.Text =
                    "最新のリリースに Stream Deck プラグインが見つかりませんでした。リリースページを開いて確認してください。";
                return;
            }

            // Asked for after the release is read, so the name the dialog offers is the name of
            // the file that is about to arrive rather than a guess at it.
            var save = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Stream Deck プラグインの保存先",
                FileName = suggestedName,
                DefaultExt = PluginAsset.Extension,
                AddExtension = true,
                Filter = $"Stream Deck プラグイン (*{PluginAsset.Extension})|*{PluginAsset.Extension}",
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

            var progress = new Progress<int>(percent => PluginStatusText.Text = $"ダウンロード中… {percent}%");
            string path = await PluginDownloader.SaveAsync(asset, save.FileName, progress);

            _downloadedPlugin = path;
            PluginStatusText.Text = $"保存しました: {path}";
            PluginOpenButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            PluginStatusText.Text = $"ダウンロードに失敗しました: {ex.Message}";
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
            PluginStatusText.Text = $"開けませんでした: {ex.Message}";
        }
    }

    private void OnReleasesClick(object sender, RoutedEventArgs e) =>
        ProjectLinks.Open(ProjectLinks.LatestRelease);

    /// <summary>
    /// Doubles as "check now" and "install now": the button becomes 更新 the moment a check finds
    /// something, and a second click on that button installs it rather than checking again. This is
    /// how someone checks on demand when the startup setting is off, so it works whether or not
    /// that checkbox above it is ticked.
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
        UpdateStatusText.Text = "確認しています…";
        try
        {
            var update = await UpdateChecker.CheckAsync();
            if (update.Available)
            {
                _pendingUpdate = update;
                UpdateButton.Content = "更新";
                UpdateStatusText.Text = $"バージョン {update.Version} が利用可能です。";
            }
            else
            {
                // Collapsing these into 最新バージョンです。 told someone whose newer release had
                // no installer attached that they were current, which is the one thing this
                // button exists to tell them the truth about.
                UpdateStatusText.Text = update.Reason switch
                {
                    UpdateUnavailableReason.NoInstaller =>
                        "新しいバージョンが公開されていますが、インストーラーが見つかりません。",
                    UpdateUnavailableReason.Unreadable =>
                        "GitHub の応答を読み取れませんでした。",
                    _ => "最新バージョンです。",
                };
            }
        }
        catch (Exception ex)
        {
            // Unlike the silent startup check, someone who pressed this button is owed a reason.
            UpdateStatusText.Text = $"確認に失敗しました: {ex.Message}";
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
        var progress = new Progress<int>(percent => UpdateButton.Content = $"ダウンロード中… {percent}%");

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
                UpdateStatusText.Text = "ダウンロードしたファイルの検証に失敗しました。実行を中止しました。";
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
                    "ダウンロードしたインストーラーの正当性を確認できません（SHA-256 が提供されていません）。実行しますか？",
                    "OpenInzone", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes)
                {
                    UpdateStatusText.Text = "更新を中止しました。";
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
            UpdateStatusText.Text = $"更新に失敗しました: {ex.Message}";

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
        UpdateButton.Content = "更新";
        UpdateButton.IsEnabled = true;
        _updateBusy = false;
    }
}
