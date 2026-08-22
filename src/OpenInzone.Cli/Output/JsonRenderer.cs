// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;
using System.Text.Json;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Cli.Output;

/// <summary>
/// The schema in docs/superpowers/specs/2026-08-23-battery-design.md. Flat values so `jq -r .left`
/// stays a one-liner, with everything that qualifies them under `detail`.
/// </summary>
public sealed class JsonRenderer(TextWriter output, bool raw = false) : IReportRenderer
{
    public void Render(IReport report)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            Write(json, report);
            json.WriteEndObject();
        }

        output.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private void Write(Utf8JsonWriter json, IReport report)
    {
        switch (report)
        {
            case BatteryReport battery:
                WriteBatteryBody(json, battery.Battery);
                break;

            case StatusReport status:
                json.WriteString("device", status.Model.Name);
                json.WriteStartObject("battery");
                WriteBatteryBody(json, status.Battery);
                json.WriteEndObject();
                WriteBalance(json, status.Balance);
                WriteVolume(json, status.Volume);
                WriteMic(json, status.Mic, status.MicLevel);
                json.WriteStartObject("sidetone");
                json.WriteNumber("value", status.Sidetone.Value);
                json.WriteEndObject();
                break;

            case BalanceReport balance:
                json.WriteNumber("value", balance.Balance.Value);
                json.WriteNumber("notch", balance.Balance.Notch);
                break;

            case VolumeReport volume:
                json.WriteNumber("value", volume.Volume.Value);
                json.WriteNumber("max", HeadphoneVolume.Max);
                json.WriteBoolean("muted", volume.Volume.Muted);
                break;

            case MicReport mic:
                json.WriteBoolean("muted", mic.Mic.Muted);
                WriteLevel(json, mic.MicLevel);
                break;

            case MicMuteReport mute:
                json.WriteBoolean("muted", mute.Mic.Muted);
                break;

            case MicLevelReport level:
                json.WriteNumber("level", level.Level);
                break;

            case DeviceListReport devices:
                json.WriteStartArray("devices");
                foreach (var device in devices.Devices)
                {
                    json.WriteStartObject();
                    json.WriteString("description", device.ToString());
                    json.WriteString("path", device.DevicePath);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                break;

            case EventReport e:
                json.WriteString("time", e.Time.ToString("HH:mm:ss"));
                json.WriteString("event", EventName(e.EventId));
                if (e.Payload is not null) Write(json, e.Payload);
                else json.WriteString("detail", e.RawHex);
                break;

            case ErrorReport error:
                json.WriteString("error", error.Code);
                json.WriteString("message", error.Message);
                break;
        }
    }

    private void WriteBatteryBody(Utf8JsonWriter json, BatteryInfo battery)
    {
        WritePart(json, "left", battery.Left);
        WritePart(json, "right", battery.Right);
        WritePart(json, "case", battery.Case);

        json.WriteStartObject("detail");
        WriteState(json, "left_state", battery.Left);
        WriteState(json, "right_state", battery.Right);
        WriteState(json, "case_state", battery.Case);
        if (battery.CaseIsSnapshot) json.WriteBoolean("case_is_snapshot", true);
        if (raw) json.WriteString("raw", TextRenderer.Hex(battery));
        json.WriteEndObject();
    }

    private static void WritePart(Utf8JsonWriter json, string name, BatteryPart part)
    {
        if (part.State is BatteryPartState.Absent) return;
        if (part.Percent is int percent) json.WriteNumber(name, percent);
        else json.WriteNull(name);
    }

    private static void WriteState(Utf8JsonWriter json, string name, BatteryPart part)
    {
        if (part.State is BatteryPartState.Absent) return;
        json.WriteString(name, part.State is BatteryPartState.Reporting ? "reporting" : "not_reporting");
    }

    private static void WriteBalance(Utf8JsonWriter json, MixBalance balance)
    {
        json.WriteStartObject("balance");
        json.WriteNumber("value", balance.Value);
        json.WriteNumber("notch", balance.Notch);
        json.WriteEndObject();
    }

    private static void WriteVolume(Utf8JsonWriter json, HeadphoneVolume volume)
    {
        json.WriteStartObject("volume");
        json.WriteNumber("value", volume.Value);
        json.WriteNumber("max", HeadphoneVolume.Max);
        json.WriteBoolean("muted", volume.Muted);
        json.WriteEndObject();
    }

    private static void WriteMic(Utf8JsonWriter json, MicVolume mic, int? level)
    {
        json.WriteStartObject("mic");
        json.WriteBoolean("muted", mic.Muted);
        WriteLevel(json, level);
        json.WriteEndObject();
    }

    private static void WriteLevel(Utf8JsonWriter json, int? level)
    {
        if (level is int value) json.WriteNumber("level", value);
        else json.WriteNull("level");
        json.WriteBoolean("level_available", level is not null);
    }

    private static string EventName(EventId eventId) => eventId switch
    {
        EventId.BatteryInfo => "battery",
        EventId.GameChatMixBalance => "balance",
        EventId.HeadphoneVolume => "volume",
        EventId.MicVolume => "mic",
        EventId.SidetoneVolume => "sidetone",
        _ => eventId.ToString(),
    };
}
