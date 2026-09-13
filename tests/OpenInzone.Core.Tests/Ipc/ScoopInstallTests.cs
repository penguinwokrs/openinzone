// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;

namespace OpenInzone.Tests.Ipc;

public class ScoopInstallTests
{
    private static Func<string, bool> Only(params string[] files) =>
        path => files.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Recognises_a_version_directory_with_install_json()
    {
        var install = ScoopInstall.TryLocate(@"C:\Users\me\scoop\apps\openinzone\1.1.2\",
            Only(@"C:\Users\me\scoop\apps\openinzone\1.1.2\install.json"));

        Assert.Equal(new ScoopInstall(@"C:\Users\me\scoop", "openinzone"), install);
        Assert.Equal(@"C:\Users\me\scoop\apps\openinzone", install!.AppDirectory);
    }

    [Fact]
    public void Recognises_the_current_junction_and_a_custom_name_and_root()
    {
        var install = ScoopInstall.TryLocate(@"D:\tools\apps\inzone-dev\current",
            Only(@"D:\tools\apps\inzone-dev\current\install.json"));

        Assert.Equal(new ScoopInstall(@"D:\tools", "inzone-dev"), install);
    }

    [Fact]
    public void A_scoop_shaped_path_without_install_json_is_not_scoop()
    {
        Assert.Null(ScoopInstall.TryLocate(@"C:\Users\me\scoop\apps\openinzone\1.1.2\", Only()));
    }

    [Fact]
    public void The_setup_install_is_not_scoop()
    {
        Assert.Null(ScoopInstall.TryLocate(@"C:\Users\me\AppData\Local\Programs\OpenInzone\",
            _ => true));
    }

    [Fact]
    public void Run_directory_is_per_app_and_version_under_local_app_data()
    {
        var install = new ScoopInstall(@"C:\Users\me\scoop", "openinzone");

        Assert.Equal(@"C:\Users\me\AppData\Local\openinzone-scoop\openinzone\1.1.4",
            install.RunDirectory(@"C:\Users\me\AppData\Local", "1.1.4"));
        Assert.Equal(@"C:\Users\me\scoop\apps\openinzone\current", install.CurrentDirectory);
    }

    [Theory]
    [InlineData(@"C:\s\apps\openinzone\1.1.3")]    // Scoop still points here
    [InlineData(@"C:\S\APPS\OPENINZONE\1.1.3\")]   // same place, spelled differently
    [InlineData(null)]                             // mid-update: current unlinked
    [InlineData(@"C:\s\apps\openinzone\1.1.4")]    // new version, tray not there yet
    public void Does_not_move_on_until_the_new_version_is_there(string? current)
    {
        Assert.False(ScoopInstall.HasMovedOn(@"C:\s\apps\openinzone\1.1.3", current, _ => false));
    }

    [Fact]
    public void Moves_on_once_current_points_at_another_version_with_a_tray()
    {
        Assert.True(ScoopInstall.HasMovedOn(@"C:\s\apps\openinzone\1.1.3", @"C:\s\apps\openinzone\1.1.4\",
            p => p == @"C:\s\apps\openinzone\1.1.4\inzonetray.exe"));
    }

    [Fact]
    public void Script_also_stops_processes_running_through_the_junctions()
    {
        string script = new ScoopInstall(@"C:\Users\me\scoop", "openinzone").BuildUpdateScript(1);

        Assert.Contains("'openinzone-scoop\\openinzone'", script);
        Assert.Contains("$runs", script);
    }

    [Fact]
    public void Script_uses_the_install_paths_and_process_id()
    {
        string script = new ScoopInstall(@"C:\Users\me\scoop", "openinzone").BuildUpdateScript(4242);

        Assert.Contains("'OpenInzone.Setup'", script);
        Assert.Contains("-Id 4242", script);
        Assert.Contains(@"'C:\Users\me\scoop\apps\scoop\current\bin\scoop.ps1'", script);
        Assert.Contains(@"'C:\Users\me\scoop\apps\openinzone\current\inzonetray.exe'", script);
    }

    [Fact]
    public void Script_still_restarts_the_tray_and_explains_after_scoop_throws()
    {
        string script = new ScoopInstall(@"C:\Users\me\scoop", "openinzone").BuildUpdateScript(1);

        // A terminating error must land in catch, not skip past Start-Process and Read-Host.
        Assert.Contains("} catch {", script);
        Assert.True(script.IndexOf("} catch {", StringComparison.Ordinal) <
                    script.IndexOf("Start-Process", StringComparison.Ordinal));
    }

    [Fact]
    public void Start_info_runs_the_encoded_script_without_the_callers_module_path()
    {
        // Inherited from a PowerShell 7 terminal, this makes Windows PowerShell load 7's modules,
        // and Get-FileHash and Read-Host stop existing - observed as a silent failed update.
        Environment.SetEnvironmentVariable("PSModulePath", @"C:\Program Files\PowerShell\7\Modules");
        var install = new ScoopInstall(@"C:\Users\me\scoop", "openinzone");

        var start = install.CreateUpdateStartInfo(4242);

        Assert.Equal("powershell.exe", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.False(start.Environment.ContainsKey("PSModulePath"));
        string encoded = start.ArgumentList[^1];
        Assert.Equal("-EncodedCommand", start.ArgumentList[^2]);
        Assert.Equal(install.BuildUpdateScript(4242),
            System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Fact]
    public void Script_doubles_every_quote_powershell_treats_as_single()
    {
        // PowerShell ends a single-quoted string on U+2018-U+201B as well as on the ASCII quote.
        string script = new ScoopInstall("C:\\Users\\O'Brien\u2019s\\scoop", "openinzone")
            .BuildUpdateScript(1);

        Assert.Contains("'C:\\Users\\O''Brien\u2019\u2019s\\scoop\\apps\\scoop\\current\\bin\\scoop.ps1'", script);
        Assert.DoesNotContain("O'Brien\u2019s", script);
    }
}
