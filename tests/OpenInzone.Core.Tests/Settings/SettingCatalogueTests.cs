// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.Protocol;
using OpenInzone.Settings;

namespace OpenInzone.Tests.Settings;

/// <summary>
/// The catalogue is the one place that says what a setting is. Before it, the same knowledge was
/// spread over a method on the device, a field on the IPC record, a command name, a case in the
/// daemon and a handler in the window, and adding a setting meant getting all five to agree.
/// </summary>
public class SettingCatalogueTests
{
    [Fact]
    public void Every_setting_is_named_once()
    {
        var ids = SettingCatalogue.All.Select(setting => setting.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void A_setting_can_be_found_by_the_id_that_travels_on_the_wire()
    {
        Assert.Equal(EventId.SidetoneVolume, SettingCatalogue.ById("sidetone")!.EventId);
        Assert.Null(SettingCatalogue.ById("no-such-setting"));
    }

    /// <summary>
    /// The ambient packet carries mode, level and voice focus together. Writing one of them has to
    /// leave the other two as the headset reported them, or changing the mode would reset the level.
    /// </summary>
    [Fact]
    public void The_three_ambient_settings_share_one_packet_and_write_only_their_own_byte()
    {
        var ambient = SettingCatalogue.All.Where(s => s.EventId == EventId.AmbientSetting).ToList();
        Assert.Equal(3, ambient.Count);

        byte[] reported = [0x02, 0x14, 0xFF, 0x00];

        Assert.Equal(new byte[] { 0x01, 0x14, 0xFF, 0x00 }, SettingCatalogue.ById("ambient-mode")!.Write(reported, 1));
        Assert.Equal(new byte[] { 0x02, 0x05, 0xFF, 0x00 }, SettingCatalogue.ById("ambient-level")!.Write(reported, 5));
        Assert.Equal(new byte[] { 0x02, 0x14, 0xFF, 0x01 }, SettingCatalogue.ById("voice-focus")!.Write(reported, 1));
    }

    [Fact]
    public void The_three_ambient_settings_read_their_own_byte()
    {
        byte[] reported = [0x02, 0x14, 0xFF, 0x01];

        Assert.Equal(2, SettingCatalogue.ById("ambient-mode")!.Read(reported));
        Assert.Equal(20, SettingCatalogue.ById("ambient-level")!.Read(reported));
        Assert.Equal(1, SettingCatalogue.ById("voice-focus")!.Read(reported));
    }

    [Fact]
    public void A_value_outside_the_setting_is_clamped_rather_than_written()
    {
        var level = SettingCatalogue.ById("ambient-level")!;
        byte[] reported = [0x02, 0x14, 0xFF, 0x00];

        Assert.Equal(new byte[] { 0x02, 0x01, 0xFF, 0x00 }, level.Write(reported, 0));
        Assert.Equal(new byte[] { 0x02, 0x14, 0xFF, 0x00 }, level.Write(reported, 99));
    }

    /// <summary>
    /// Auto power off answers 0x0F rather than 0x01, and is written back the same way. The byte is
    /// the setting's own, not a shared idea of what "on" means.
    /// </summary>
    [Fact]
    public void Auto_power_off_carries_its_own_byte_for_on()
    {
        var setting = SettingCatalogue.ById("auto-power-off")!;

        Assert.Equal(1, setting.Read([0x0F]));
        Assert.Equal(0, setting.Read([0x00]));
        Assert.Equal(new byte[] { 0x0F }, setting.Write([0x0F], 1));
        Assert.Equal(new byte[] { 0x00 }, setting.Write([0x0F], 0));
    }

    [Fact]
    public void The_other_toggles_write_one_for_on()
    {
        foreach (string id in new[] { "voice-guidance", "bluetooth-auto-switch" })
        {
            var setting = SettingCatalogue.ById(id)!;
            Assert.Equal(SettingKind.Toggle, setting.Kind);
            Assert.Equal(new byte[] { 0x01 }, setting.Write([0x00], 1));
            Assert.Equal(new byte[] { 0x00 }, setting.Write([0x01], 0));
            Assert.Equal(1, setting.Read([0x01]));
        }
    }

    [Fact]
    public void The_voice_guidance_language_is_a_choice_of_three()
    {
        var setting = SettingCatalogue.ById("voice-guidance-language")!;

        Assert.Equal(SettingKind.Choice, setting.Kind);
        Assert.Equal(0, setting.Minimum);
        Assert.Equal(2, setting.Maximum);
        Assert.Equal(new byte[] { 0x02 }, setting.Write([0x01], 2));
    }

    /// <summary>
    /// A reply shorter than the setting reads from is a device saying something this build does not
    /// understand. It answers with the bottom of the range rather than throwing, because settings
    /// are read on the connection's own thread where an exception drops the link.
    /// </summary>
    [Fact]
    public void A_reply_too_short_to_read_from_does_not_throw()
    {
        Assert.Equal(0, SettingCatalogue.ById("voice-focus")!.Read([0x02, 0x14]));
        Assert.Equal(0, SettingCatalogue.ById("sidetone")!.Read([]));
    }

    /// <summary>
    /// A setting that is the whole packet has nothing to preserve, so a write is composed rather
    /// than read back first. That read was a round trip spent for nothing — and one more chance for
    /// a bad moment on the link to drop the headset on the way to ticking a checkbox.
    /// </summary>
    [Fact]
    public void A_setting_that_is_the_whole_packet_needs_no_reading_first()
    {
        foreach (string id in new[]
        {
            "auto-power-off", "voice-guidance", "bluetooth-auto-switch", "voice-guidance-language",
        })
        {
            Assert.True(SettingCatalogue.ById(id)!.OwnsPacket, id);
        }
    }

    /// <summary>
    /// And everything that shares its packet, or leaves a byte in it to the headset, does need one.
    /// </summary>
    [Fact]
    public void Everything_with_something_to_preserve_does_not()
    {
        foreach (string id in new[] { "ambient-mode", "ambient-level", "voice-focus", "sidetone" })
            Assert.False(SettingCatalogue.ById(id)!.OwnsPacket, id);
    }

    /// <summary>Owning the packet has to mean owning every byte of it, or a write would lose one.</summary>
    [Fact]
    public void Nothing_claims_a_packet_it_shares()
    {
        foreach (var setting in SettingCatalogue.All.Where(s => s.OwnsPacket))
            Assert.Single(SettingCatalogue.ForEvent(setting.EventId));
    }

    /// <summary>
    /// Sidetone's second byte is the headset's own reading — 0xFF on INZONE Buds — and goes back
    /// untouched, as the headphone volume's percent byte does.
    /// </summary>
    [Fact]
    public void Sidetone_echoes_the_byte_it_does_not_own()
    {
        Assert.Equal(new byte[] { 0x05, 0xFF }, SettingCatalogue.ById("sidetone")!.Write([0x00, 0xFF], 5));
    }
}

/// <summary>
/// The catalogue lives in the core and the feature ids live in the published contract, because a
/// client - the Stream Deck plugin, which ships trimmed - has no business carrying the HID stack
/// to learn what a setting is called. Two lists is the price of that, so they are pinned together
/// here rather than left to drift.
/// </summary>
public class SettingIdTests
{
    [Fact]
    public void Every_setting_the_core_describes_has_a_feature_id_on_the_wire()
    {
        var missing = SettingCatalogue.All
            .Select(setting => setting.Id)
            .Except(FeatureIds.All)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// The other way round has to hold too, minus the features that are not settings: a feature id
    /// nothing answers for would be a control a client draws and nothing ever fills.
    /// </summary>
    [Fact]
    public void Every_feature_id_is_either_a_setting_or_part_of_the_panel()
    {
        string[] panel =
        [
            FeatureIds.Balance, FeatureIds.Volume, FeatureIds.MicMute,
            FeatureIds.MicLevel, FeatureIds.Battery,
        ];

        var unaccounted = FeatureIds.All
            .Except(panel)
            .Except(SettingCatalogue.All.Select(setting => setting.Id))
            .ToList();

        Assert.Empty(unaccounted);
    }
}
