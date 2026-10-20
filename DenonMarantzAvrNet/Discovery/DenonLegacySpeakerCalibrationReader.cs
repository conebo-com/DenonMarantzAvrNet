using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DenonMarantzAvrNet.Models;
using DenonMarantzAvrNet.Transport;

namespace DenonMarantzAvrNet.Discovery;

/// <summary>
/// Reads the actual persistent speaker setup from legacy Denon HTML pages.
/// Only HTTP GET is used. No JavaScript, form submission, or guessed Set commands.
/// Unknown channel codes are retained verbatim. Does not depend on model profiles.
/// </summary>
internal static class DenonLegacySpeakerCalibrationReader
{
    internal const string LevelsPath = "/SETUP/SPEAKERS/LEVELS/d_speakersetup.asp";
    internal const string DistancesPath = "/SETUP/SPEAKERS/DISTANCES/d_speakersetup.asp";
    private const int MaximumHtmlLength = 256 * 1024;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex RowPattern = new(@"<tr\b[^>]*>(?<row>.*?)</tr\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex InputPattern = new(@"<input\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex AttributePattern = new(
        "(?<key>[a-zA-Z_][a-zA-Z0-9_:-]*)\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)'|(?<plain>[^\\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex FirstCellPattern = new(@"<td\b[^>]*>(?<text>.*?)</td\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex StripTagsPattern = new(@"<[^>]*>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex CollapseSpacesPattern = new(@"\s+",
        RegexOptions.CultureInvariant, MatchTimeout);
    private static readonly Regex ValidChannelSuffixPattern = new(@"^[a-zA-Z0-9]{1,12}$",
        RegexOptions.CultureInvariant, MatchTimeout);

    internal static async Task<DenonLegacySpeakerCalibrationSnapshot> ReadAsync(
        string host, int port, DenonHttpTransport transport, CancellationToken cancellationToken)
    {
        NetFrameworkCompat.ThrowIfNull(transport, nameof(transport));
        // Sequential requests avoid overloading older receiver web servers.
        var levels = await ReadLevelsAsync(host, port, transport, cancellationToken)
            .ConfigureAwait(false);
        var distances = await ReadDistancesAsync(host, port, transport, cancellationToken)
            .ConfigureAwait(false);
        return new DenonLegacySpeakerCalibrationSnapshot(
            port, distances.Unit, levels, distances.Speakers);
    }

    internal static async Task<IReadOnlyList<DenonLegacySpeakerCalibrationLevel>> ReadLevelsAsync(
        string host, int port, DenonHttpTransport transport, CancellationToken cancellationToken)
    {
        var html = await transport.GetStringAsync(host, port, LevelsPath, cancellationToken)
            .ConfigureAwait(false);
        return ParseLevels(html);
    }

    internal static async Task<LegacyDistancesPage> ReadDistancesAsync(
        string host, int port, DenonHttpTransport transport, CancellationToken cancellationToken)
    {
        var html = await transport.GetStringAsync(host, port, DistancesPath, cancellationToken)
            .ConfigureAwait(false);
        return ParseDistances(html);
    }

    internal static IReadOnlyList<DenonLegacySpeakerCalibrationLevel> ParseLevels(string html)
    {
        EnsureCorrectPage(html, "Speakers/Levels");
        var levels = new List<DenonLegacySpeakerCalibrationLevel>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in RowPattern.Matches(html))
        {
            var body = row.Groups["row"].Value;
            foreach (Match input in InputPattern.Matches(body))
            {
                var attributes = ParseAttributes(input.Value);
                if (!attributes.TryGetValue("name", out var name) ||
                    !name.StartsWith("textCV", StringComparison.OrdinalIgnoreCase) ||
                    !attributes.TryGetValue("type", out var inputType) ||
                    !inputType.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                    continue;

                var nativeCode = name.Substring("textCV".Length).ToUpperInvariant();
                ValidateChannel(nativeCode, codes);
                var value = ReadNumber(attributes, "value", "calibration level");
                if (value < -12 || value > 12 || Math.Abs(value * 2 - Math.Round(value * 2)) > 1e-6)
                    throw new InvalidDataException("Legacy calibration level outside the advertised -12..12 dB / 0.5 dB range.");
                levels.Add(new DenonLegacySpeakerCalibrationLevel(nativeCode,
                    ReadDisplayName(body, nativeCode), name, value));
            }
        }

        if (levels.Count == 0)
            throw new InvalidDataException("No persistent calibration levels found in the legacy speaker setup page.");
        return levels;
    }

    internal static LegacyDistancesPage ParseDistances(string html)
    {
        EnsureCorrectPage(html, "Speakers/Distances");
        var units = new List<string>();
        foreach (Match input in InputPattern.Matches(html))
        {
            var attributes = ParseAttributes(input.Value);
            if (attributes.TryGetValue("name", out var name) &&
                name.Equals("radioDelayTimeMode", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(input.Value, @"\bchecked(?:\s|=|>|$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout) &&
                attributes.TryGetValue("value", out var unitCode))
                units.Add(unitCode);
        }

        if (units.Count != 1 || !(units[0].Equals("M", StringComparison.OrdinalIgnoreCase) ||
                                  units[0].Equals("F", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Legacy distance page has no unambiguous selected unit.");
        var unit = units[0].Equals("M", StringComparison.OrdinalIgnoreCase)
            ? DenonSpeakerDistanceUnit.Meters : DenonSpeakerDistanceUnit.Feet;

        var speakers = new List<DenonLegacySpeakerCalibrationDistance>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in RowPattern.Matches(html))
        {
            var body = row.Groups["row"].Value;
            foreach (Match input in InputPattern.Matches(body))
            {
                var attributes = ParseAttributes(input.Value);
                if (!attributes.TryGetValue("name", out var name) ||
                    !name.StartsWith("textDelayTime", StringComparison.OrdinalIgnoreCase) ||
                    !attributes.TryGetValue("type", out var type) ||
                    !type.Equals("text", StringComparison.OrdinalIgnoreCase))
                    continue;

                var nativeCode = name.Substring("textDelayTime".Length).ToUpperInvariant();
                ValidateChannel(nativeCode, codes);
                var original = ReadNumber(attributes, "value", "speaker distance");
                if (original < 0 || original > 500)
                    throw new InvalidDataException("Legacy speaker distance outside a plausible range.");
                var meters = unit == DenonSpeakerDistanceUnit.Feet ? original * 0.3048 : original;
                if (meters > 100)
                    throw new InvalidDataException("Normalized speaker distance outside a plausible range.");
                speakers.Add(new DenonLegacySpeakerCalibrationDistance(nativeCode,
                    ReadDisplayName(body, nativeCode), name, meters, original, unit));
            }
        }
        if (speakers.Count == 0)
            throw new InvalidDataException("No speaker distances found in the legacy speaker setup page.");
        return new LegacyDistancesPage(unit, speakers);
    }

    private static void EnsureCorrectPage(string html, string pageMarker)
    {
        if (string.IsNullOrEmpty(html) || html.Length > MaximumHtmlLength ||
            html.IndexOf(pageMarker, StringComparison.OrdinalIgnoreCase) < 0 ||
            (html.IndexOf("name=\"spsetup\"", StringComparison.OrdinalIgnoreCase) < 0 &&
             html.IndexOf("name='spsetup'", StringComparison.OrdinalIgnoreCase) < 0) ||
            html.IndexOf("s_speakersetup.asp", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidDataException("Unexpected legacy speaker setup page; do not treat it as calibration data.");

        // A receiver may redirect when the setup is locked or Pure Direct is on.
        foreach (Match input in InputPattern.Matches(html))
        {
            var attrs = ParseAttributes(input.Value);
            if (attrs.TryGetValue("name", out var name) &&
                (name.Equals("setPureDirectOn", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("setSetupLock", StringComparison.OrdinalIgnoreCase)) &&
                attrs.TryGetValue("value", out var value) &&
                value.Equals("ON", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Legacy speaker setup is currently locked or blocked by Pure Direct.");
        }
    }

    private static void ValidateChannel(string code, HashSet<string> codes)
    {
        if (!ValidChannelSuffixPattern.IsMatch(code) || !codes.Add(code))
            throw new InvalidDataException("Duplicate or invalid native speaker channel code in legacy setup page.");
    }

    private static Dictionary<string, string> ParseAttributes(string tag)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in AttributePattern.Matches(tag))
        {
            var match = attribute.Groups;
            var value = match["double"].Success ? match["double"].Value :
                match["single"].Success ? match["single"].Value : match["plain"].Value;
            attrs[match["key"].Value] = WebUtility.HtmlDecode(value);
        }
        return attrs;
    }

    private static double ReadNumber(IReadOnlyDictionary<string, string> attrs, string key, string description)
    {
        if (!attrs.TryGetValue(key, out var text) ||
            !double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value))
            throw new InvalidDataException("Invalid numeric " + description + " in legacy HTML input.");
        return value;
    }

    private static string ReadDisplayName(string row, string fallback)
    {
        var cell = FirstCellPattern.Match(row);
        if (!cell.Success)
            return fallback;
        var label = WebUtility.HtmlDecode(StripTagsPattern.Replace(cell.Groups["text"].Value, ""));
        label = CollapseSpacesPattern.Replace(label.Replace('\u00a0', ' '), " ").Trim();
        return label.Length is > 0 and <= 80 ? label : fallback;
    }

    internal sealed record LegacyDistancesPage(
        DenonSpeakerDistanceUnit Unit,
        IReadOnlyList<DenonLegacySpeakerCalibrationDistance> Speakers);
}
