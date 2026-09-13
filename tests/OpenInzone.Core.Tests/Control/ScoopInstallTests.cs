// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

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
    public void Script_uses_the_install_paths_and_process_id()
    {
        string script = new ScoopInstall(@"C:\Users\me\scoop", "openinzone").BuildUpdateScript(4242);

        Assert.Contains("'OpenInzone.Setup'", script);
        Assert.Contains("-Id 4242", script);
        Assert.Contains(@"'C:\Users\me\scoop\apps\scoop\current\bin\scoop.ps1'", script);
        Assert.Contains(@"'C:\Users\me\scoop\apps\openinzone\current\inzonetray.exe'", script);
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
