// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;

namespace OpenInzone.Cli.Output;

/// <summary>
/// The aligned columns the tool has always printed. Errors go to <paramref name="error"/>, which
/// is where they have always gone; only the JSON renderer puts them on stdout.
/// </summary>
public sealed class TextRenderer(TextWriter output, TextWriter error, bool raw = false) : IReportRenderer
{
    public void Render(IReport report)
    {
        switch (report)
        {
            case StatusReport status:
                RenderStatus(status);
                break;
            case BatteryReport battery:
                output.WriteLine(battery.Battery.ToString());
                if (raw) output.WriteLine($"raw    {Hex(battery.Battery)}");
                break;
            case BalanceReport balance:
                output.WriteLine(balance.Balance.ToString());
                break;
            case VolumeReport volume:
                output.WriteLine(volume.Volume.ToString());
                break;
            case MicReport mic:
                output.WriteLine(Mic(mic));
                break;
            case DeviceListReport devices:
                foreach (var device in devices.Devices)
                {
                    output.WriteLine(device.ToString());
                    output.WriteLine($"  {device.DevicePath}");
                }
                break;
            case EventReport e:
                output.WriteLine($"{e.Time:HH:mm:ss}  {e.EventId,-22} {e.Detail}");
                break;
            case ErrorReport failure:
                error.WriteLine(failure.Message);
                break;
        }
    }

    private void RenderStatus(StatusReport status)
    {
        output.WriteLine($"Device       {status.Model.Name}");
        if (status.Model.IsEarbuds)
        {
            output.WriteLine(
                $"Serial       L {status.Model.LeftSerial} / R {status.Model.RightSerial} / dongle {status.Model.DongleSerial}");
        }
        output.WriteLine($"Battery      {status.Battery}");
        output.WriteLine($"Balance      {status.Balance}");
        output.WriteLine($"Volume       {status.Volume}");
        output.WriteLine($"Microphone   {Mic(new MicReport(status.Mic, status.MicLevel))}");
        output.WriteLine($"Sidetone     {status.Sidetone}");
        if (raw) output.WriteLine($"Battery raw  {Hex(status.Battery)}");
    }

    private static string Mic(MicReport mic)
    {
        string mute = mic.Mic.Muted ? "muted" : "unmuted";
        return mic.MicLevel is int level ? $"{mute}, level {level}%" : mute;
    }

    internal static string Hex(BatteryInfo battery) => string.Join(' ',
        new[]
        {
            battery.LeftStatus, battery.LeftPercent,
            battery.RightStatus, battery.RightPercent,
            battery.CaseStatus, battery.CasePercent,
        }.Select(b => b.ToString("X2")));
}
