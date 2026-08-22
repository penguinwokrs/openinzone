// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Hid;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Cli.Output;

/// <summary>
/// What a command produced, before anything decides how it looks. Commands build one of these and
/// a renderer draws it, so a new output format costs one renderer rather than one branch per command.
/// </summary>
public interface IReport;

public sealed record BatteryReport(BatteryInfo Battery) : IReport;

public sealed record StatusReport(
    ModelInfo Model,
    BatteryInfo Battery,
    MixBalance Balance,
    HeadphoneVolume Volume,
    MicVolume Mic,
    int? MicLevel,
    SidetoneVolume Sidetone) : IReport;

public sealed record BalanceReport(MixBalance Balance) : IReport;

public sealed record VolumeReport(HeadphoneVolume Volume) : IReport;

/// <summary>`inzone mic` with no arguments, and the microphone line of `inzone status`.</summary>
public sealed record MicReport(MicVolume Mic, int? MicLevel) : IReport;

/// <summary>
/// `inzone mic mute` / `unmute` / `toggle`. The headset's own flag and nothing else — this
/// command has never reported the Windows capture level alongside it.
/// </summary>
public sealed record MicMuteReport(MicVolume Mic) : IReport;

/// <summary>`inzone mic 50` / `mic +5`. The Windows capture endpoint level on its own.</summary>
public sealed record MicLevelReport(int Level) : IReport;

public sealed record DeviceListReport(IReadOnlyList<HidDeviceInfo> Devices) : IReport;

/// <summary>
/// One line of `inzone watch`. <paramref name="Payload"/> is the report the matching command
/// would have produced, so each renderer can draw a notification the way it draws that command.
/// It is null for an event this tool has no decoder for, and <paramref name="RawHex"/> carries
/// the undecoded bytes for that case.
/// </summary>
public sealed record EventReport(DateTime Time, EventId EventId, IReport? Payload, string RawHex) : IReport;

public sealed record ErrorReport(string Code, string Message) : IReport;
