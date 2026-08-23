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
                if (raw) output.WriteLine($"raw    {HexFormat.Battery(battery.Battery)}");
                break;
            case BalanceReport balance:
                output.WriteLine(balance.Balance.ToString());
                break;
            case VolumeReport volume:
                output.WriteLine(volume.Volume.ToString());
                break;
            case SidetoneReport sidetone:
                output.WriteLine(sidetone.Sidetone.ToString());
                break;
            case MicReport mic:
                output.WriteLine(Mic(mic));
                break;
            case MicMuteReport mute:
                output.WriteLine(mute.Mic.ToString());
                break;
            case MicLevelReport level:
                output.WriteLine($"level {level.Level}%");
                break;
            case DeviceListReport devices:
                foreach (var device in devices.Devices)
                {
                    output.WriteLine(device.ToString());
                    output.WriteLine($"  {device.DevicePath}");
                }
                break;
            case EventReport e:
                output.WriteLine($"{e.Time:HH:mm:ss}  {e.EventId,-22} {Detail(e)}");
                break;
            case ErrorReport failure:
                error.WriteLine(failure.Message);
                break;
            default:
                throw new NotSupportedException($"No rendering for {report.GetType().Name}");
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
        if (raw) output.WriteLine($"Battery raw  {HexFormat.Battery(status.Battery)}");
    }

    private static string Mic(MicReport mic)
    {
        string mute = mic.Mic.Muted ? "muted" : "unmuted";
        return mic.MicLevel is int level ? $"{mute}, level {level}%" : mute;
    }

    /// <summary>
    /// One line per event, so a battery reading arriving as a notification appends its hex under
    /// `--raw` rather than adding a second line: watching bytes change during a notification is
    /// exactly what `--raw` is for.
    /// </summary>
    /// <summary>
    /// The decoded value, and under --raw the bytes it was decoded from.
    /// </summary>
    /// <remarks>
    /// --raw used to reach only the battery, which made it useless for the thing it is for:
    /// watching what INZONE Hub sends in order to work out a setting this project cannot decode
    /// yet. A decoded line was the one place the bytes were hidden, and those are exactly the
    /// lines worth comparing against a value seen in Hub's own window.
    /// </remarks>
    private string Detail(EventReport e)
    {
        string decoded = e.Payload switch
        {
            BatteryReport battery => battery.Battery.ToString(),
            BalanceReport balance => balance.Balance.ToString(),
            VolumeReport volume => volume.Volume.ToString(),
            MicMuteReport mic => mic.Mic.ToString(),
            SidetoneReport sidetone => sidetone.Sidetone.ToString(),
            _ => e.RawHex,
        };

        // Nothing to repeat when the line is already the bytes.
        return raw && e.Payload is not null ? $"{decoded}  raw {e.RawHex}" : decoded;
    }
}
