// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Cli;
using OpenInzone.Cli.Output;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Tests;

/// <summary>
/// No arguments used to be treated as a device failure (exit 1) and always printed the text usage
/// block, even under `--json`. It is a usage failure like any other: exit 2, and well-formed JSON
/// on the error path.
/// </summary>
public class ProgramTests
{
    [Fact]
    public void NoArgumentsUnderJsonRendersAnErrorReportAndReturnsTwo()
    {
        var writer = new StringWriter { NewLine = "\n" };
        var renderer = new JsonRenderer(writer);

        int exitCode = Program.NoArguments(renderer, json: true);

        Assert.Equal(2, exitCode);
        string json = writer.ToString();
        Assert.Contains("\"error\":\"usage\"", json);
        Assert.DoesNotContain("Usage:", json);
    }

    [Fact]
    public void NoArgumentsInTextModePrintsUsageAndReturnsTwo()
    {
        var originalOut = Console.Out;
        var writer = new StringWriter { NewLine = "\n" };
        Console.SetOut(writer);
        try
        {
            var renderer = new TextRenderer(writer, writer);

            int exitCode = Program.NoArguments(renderer, json: false);

            Assert.Equal(2, exitCode);
            Assert.Contains("Usage:", writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}

/// <summary>
/// `WatchFilter` has always accepted "sidetone" and `JsonRenderer.EventName` has always mapped
/// it, but `Payload` had no arm for it: the event reached a consumer with no decoded value.
/// </summary>
public class WatchPayloadTests
{
    [Fact]
    public void DecodesASidetoneNotification()
    {
        var payload = Program.Payload(EventId.SidetoneVolume, [30, 100]);

        var sidetone = Assert.IsType<SidetoneReport>(payload);
        Assert.Equal(30, sidetone.Sidetone.Value);
        Assert.Equal(100, sidetone.Sidetone.Percent);
    }

    [Fact]
    public void HasNoDecoderForASidetonePayloadShorterThanTwoBytes()
    {
        Assert.Null(Program.Payload(EventId.SidetoneVolume, [30]));
    }
}

/// <summary>
/// Scoop refuses to update while any program runs from its app directory, and a watch left open in
/// a terminal is the one that nothing else can move out of the way. Scoop's own message only names
/// the process, so the watch says up front what it is holding up and what to do.
/// </summary>
public class WatchBannerTests
{
    [Fact]
    public void Says_only_how_to_stop_outside_scoop()
    {
        Assert.Equal("Watching INZONE Buds. Press Ctrl+C to stop.", Program.WatchBanner("INZONE Buds", null));
    }

    [Fact]
    public void Says_what_it_holds_up_on_a_scoop_install()
    {
        string banner = Program.WatchBanner("INZONE Buds", new OpenInzone.Ipc.ScoopInstall(@"C:\scoop", "openinzone"));

        Assert.StartsWith("Watching INZONE Buds. Press Ctrl+C to stop.", banner);
        Assert.Contains("'scoop update openinzone'", banner);
    }
}
