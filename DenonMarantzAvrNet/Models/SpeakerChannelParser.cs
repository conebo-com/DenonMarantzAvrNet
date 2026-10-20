namespace DenonMarantzAvrNet.Models;

/// <summary>Converts Denon's active-speaker abbreviations into <see cref="SpeakerChannel"/> flags.</summary>
public static class SpeakerChannelParser
{
    private static readonly IReadOnlyDictionary<string, SpeakerChannel> Map =
        new Dictionary<string, SpeakerChannel>(StringComparer.OrdinalIgnoreCase)
        {
            ["FL"] = SpeakerChannel.FrontLeft, ["FR"] = SpeakerChannel.FrontRight,
            ["C"] = SpeakerChannel.Center, ["SW"] = SpeakerChannel.Subwoofer,
            ["SW1"] = SpeakerChannel.Subwoofer, ["SW2"] = SpeakerChannel.Subwoofer2,
            ["SW3"] = SpeakerChannel.Subwoofer3, ["SW4"] = SpeakerChannel.Subwoofer4,
            ["SL"] = SpeakerChannel.SurroundLeft, ["SR"] = SpeakerChannel.SurroundRight,
            ["SB"] = SpeakerChannel.SurroundBack,
            ["SBL"] = SpeakerChannel.SurroundBackLeft, ["SBR"] = SpeakerChannel.SurroundBackRight,
            ["FWL"] = SpeakerChannel.FrontWideLeft, ["FWR"] = SpeakerChannel.FrontWideRight,
            ["FHL"] = SpeakerChannel.FrontHeightLeft, ["FHR"] = SpeakerChannel.FrontHeightRight,
            ["TFL"] = SpeakerChannel.TopFrontLeft, ["TFR"] = SpeakerChannel.TopFrontRight,
            ["TML"] = SpeakerChannel.TopMiddleLeft, ["TMR"] = SpeakerChannel.TopMiddleRight,
            ["TRL"] = SpeakerChannel.TopRearLeft, ["TRR"] = SpeakerChannel.TopRearRight,
            ["RHL"] = SpeakerChannel.RearHeightLeft, ["RHR"] = SpeakerChannel.RearHeightRight,
            ["SHL"] = SpeakerChannel.SurroundHeightLeft, ["SHR"] = SpeakerChannel.SurroundHeightRight,
            ["CH"] = SpeakerChannel.CenterHeight, ["TS"] = SpeakerChannel.TopSurround,
            ["FDL"] = SpeakerChannel.FrontDolbyLeft, ["FDR"] = SpeakerChannel.FrontDolbyRight,
            ["SDL"] = SpeakerChannel.SurroundDolbyLeft, ["SDR"] = SpeakerChannel.SurroundDolbyRight,
            ["BDL"] = SpeakerChannel.BackDolbyLeft, ["BDR"] = SpeakerChannel.BackDolbyRight
        };

    /// <summary>Returns the canonical Denon channel abbreviation or an enum name for an unknown flag.</summary>
    public static string ToNativeCode(SpeakerChannel channel)
    {
        switch (channel)
        {
            case SpeakerChannel.FrontLeft: return "FL";
            case SpeakerChannel.FrontRight: return "FR";
            case SpeakerChannel.Center: return "C";
            case SpeakerChannel.Subwoofer: return "SW";
            case SpeakerChannel.Subwoofer2: return "SW2";
            case SpeakerChannel.Subwoofer3: return "SW3";
            case SpeakerChannel.Subwoofer4: return "SW4";
            case SpeakerChannel.SurroundLeft: return "SL";
            case SpeakerChannel.SurroundRight: return "SR";
            case SpeakerChannel.SurroundBack: return "SB";
            case SpeakerChannel.SurroundBackLeft: return "SBL";
            case SpeakerChannel.SurroundBackRight: return "SBR";
            case SpeakerChannel.FrontWideLeft: return "FWL";
            case SpeakerChannel.FrontWideRight: return "FWR";
            case SpeakerChannel.FrontHeightLeft: return "FHL";
            case SpeakerChannel.FrontHeightRight: return "FHR";
            case SpeakerChannel.TopFrontLeft: return "TFL";
            case SpeakerChannel.TopFrontRight: return "TFR";
            case SpeakerChannel.TopMiddleLeft: return "TML";
            case SpeakerChannel.TopMiddleRight: return "TMR";
            case SpeakerChannel.TopRearLeft: return "TRL";
            case SpeakerChannel.TopRearRight: return "TRR";
            case SpeakerChannel.RearHeightLeft: return "RHL";
            case SpeakerChannel.RearHeightRight: return "RHR";
            case SpeakerChannel.SurroundHeightLeft: return "SHL";
            case SpeakerChannel.SurroundHeightRight: return "SHR";
            case SpeakerChannel.FrontDolbyLeft: return "FDL";
            case SpeakerChannel.FrontDolbyRight: return "FDR";
            case SpeakerChannel.SurroundDolbyLeft: return "SDL";
            case SpeakerChannel.SurroundDolbyRight: return "SDR";
            case SpeakerChannel.BackDolbyLeft: return "BDL";
            case SpeakerChannel.BackDolbyRight: return "BDR";
            case SpeakerChannel.CenterHeight: return "CH";
            case SpeakerChannel.TopSurround: return "TS";
            default: return channel.ToString();
        }
    }

    /// <summary>Combines all known channel codes into a flag value. Unknown codes are ignored.</summary>
    public static SpeakerChannel Parse(IEnumerable<string> codes)
    {
        NetFrameworkCompat.ThrowIfNull(codes, nameof(codes));

        var result = SpeakerChannel.None;
        foreach (var code in codes)
        {
            if (!string.IsNullOrWhiteSpace(code) && Map.TryGetValue(code, out var channel))
            {
                result |= channel;
            }
        }

        return result;
    }
}
