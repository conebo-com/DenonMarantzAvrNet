using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using DenonMarantzAvrNet.Models;
using DenonMarantzAvrNet.Protocol;

namespace DenonMarantzAvrNet.Profiles;

/// <summary>
/// Reads actual AJAX speaker indices without assuming that index numbers on different
/// pages refer to the same channel. Unsupported optional pages are reported, not hidden.
/// No SET or other write request is performed.
/// </summary>
internal static class DenonSpeakerDiscoveryReader
{
    internal static async Task<DenonSpeakerDiscoveryReport> ReadAsync(
        DenonProfileContext context,
        DenonSpeakerAjaxOptions options,
        string profileId,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var levels = Array.Empty<DenonSpeakerDiscoveryEntry>();
        var distances = Array.Empty<DenonSpeakerDiscoveryEntry>();
        var configuration = Array.Empty<DenonSpeakerDiscoveryEntry>();
        var layout = Array.Empty<DenonSpeakerDiscoveryEntry>();
        var menu = Array.Empty<DenonSpeakerDiscoveryMenuItem>();
        string? unit = null;
        string? step = null;

        // The AJAX port is deliberately provided by the selected receiver profile.
        // We do not guess that a successful HTTP request proves support for all types.
        foreach (var type in new[] { 5, 4, 1, 3, 15 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var xml = await context.HttpTransport.GetStringAsync(
                    context.Host,
                    options.Scheme,
                    options.Port,
                    DenonEndpoints.SpeakerConfiguration(type),
                    options.AllowUntrustedServerCertificate,
                    cancellationToken).ConfigureAwait(false);

                switch (type)
                {
                    case 5:
                        levels = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
                            xml, "Levels", "Speaker", profileId, isLevel: true);
                        if (levels.Length == 0) warnings.Add("type=5 returned no level indices.");
                        break;
                    case 4:
                        distances = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
                            xml, "Distances", "Speaker", profileId, isLevel: false);
                        (unit, step) = DenonSpeakerDiscoveryParser.ParseDistanceMetadata(xml);
                        if (distances.Length == 0) warnings.Add("type=4 returned no distance indices.");
                        break;
                    case 1:
                        menu = DenonSpeakerDiscoveryParser.ParseMenu(xml);
                        break;
                    case 3:
                        configuration = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
                            xml, "SpeakerConfig", "Speaker", profileId, isLevel: null);
                        if (configuration.Length == 0) warnings.Add("type=3 returned no configuration indices.");
                        break;
                    case 15:
                        layout = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
                            xml, "", "Item", profileId, isLevel: null);
                        if (layout.Length == 0) warnings.Add("type=15 returned no indexed layout groups.");
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or
                                                 InvalidDataException or XmlException or
                                                 TaskCanceledException)
            {
                warnings.Add($"Speaker AJAX type={type}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (levels.Length == 0 && distances.Length == 0)
        {
            warnings.Add("No level/distance indices were returned. This is not proof that the receiver lacks speakers.");
        }

        return new DenonSpeakerDiscoveryReport(
            DateTimeOffset.UtcNow, profileId, levels, distances, configuration,
            layout, menu, unit, step, warnings);
    }
}

/// <summary>Pure XML parser for captured, read-only AJAX fixtures and live responses.</summary>
internal static class DenonSpeakerDiscoveryParser
{
    internal static DenonSpeakerDiscoveryEntry[] ParseSpeakerEntries(
        string xml,
        string section,
        string itemName,
        string profileId,
        bool? isLevel)
    {
        var root = ParseXml(xml).Root ?? throw new InvalidDataException("Missing XML root.");
        var owner = string.IsNullOrEmpty(section)
            ? root
            : root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == section)
              ?? throw new InvalidDataException($"Missing {section} section.");

        // Only <Speaker> children of the data list, NOT entries from selectable option lists.
        // For type=15 the <Item> elements are layout GROUP indices, not speaker indices.
        IEnumerable<XElement> elements;
        if (itemName == "Item")
        {
            // Ignore nested selectable-value <Item> elements: only a layout
            // item with its own Config/Conn properties is a group index.
            elements = owner.Descendants().Where(e => e.Name.LocalName == "Item" &&
                e.Attribute("index") != null &&
                e.Elements().Any(child => child.Name.LocalName == "Config" ||
                                          child.Name.LocalName == "Conn"));
        }
        else if (section == "SpeakerConfig")
        {
            // SpeakerConfig uses direct <Speaker> children, whose <List> children
            // are selectable values, not speaker lists.
            elements = owner.Elements().Where(e => e.Name.LocalName == "Speaker");
        }
        else
        {
            elements = owner.Elements().Where(e => e.Name.LocalName == "List")
                .SelectMany(list => list.Elements().Where(e => e.Name.LocalName == "Speaker"));
        }

        var output = new List<DenonSpeakerDiscoveryEntry>();
        var indices = new HashSet<int>();
        foreach (var element in elements)
        {
            var indexText = (string?)element.Attribute("index");
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                index < 0)
            {
                throw new InvalidDataException($"Invalid {itemName} index: '{indexText}'.");
            }
            if (!indices.Add(index))
            {
                throw new InvalidDataException($"Duplicate {itemName} index {index}.");
            }

            var displayText = (string?)element.Attribute("display");
            int? display = int.TryParse(displayText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var d) ? d : null;

            // Some firmware may include an explicit label. Never treat a display code
            // (e.g. display=3) as a channel identifier or invent a speaker name.
            var receiverLabel = (string?)element.Attribute("name")
                ?? (string?)element.Attribute("label")
                ?? (string?)element.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "Name" || e.Name.LocalName == "Label");
            if (string.IsNullOrWhiteSpace(receiverLabel)) receiverLabel = null;

            var rawValue = itemName == "Item"
                ? $"Config={ReadScalarChild(element, "Config")}; " +
                  $"Conn={ReadScalarChild(element, "Conn")}" 
                : element.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value
                  ?? element.Value.Trim();

            // Existing tables were derived from X6800H, and are merely hints even there.
            SpeakerChannel? hint = null;
            if (string.Equals(profileId, "avc-x6800h", StringComparison.OrdinalIgnoreCase))
            {
                if (isLevel == true &&
                    DenonSpeakerPresetIndexConverter.TryToSpeakerChannel(index, out var levelChannel))
                    hint = levelChannel;
                else if (isLevel == false &&
                         DenonSpeakerDistanceIndexConverter.TryToSpeakerChannel(index, out var distanceChannel))
                    hint = distanceChannel;
            }

            output.Add(new DenonSpeakerDiscoveryEntry(index, rawValue, display, receiverLabel, hint));
        }

        return output.OrderBy(e => e.NativeIndex).ToArray();
    }

    private static string? ReadScalarChild(XElement parent, string name)
    {
        var child = parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        if (child == null) return null;
        return child.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value.Trim()
            ?? (child.HasElements ? null : child.Value.Trim());
    }

    internal static DenonSpeakerDiscoveryMenuItem[] ParseMenu(string xml)
    {
        var root = ParseXml(xml).Root ?? throw new InvalidDataException("Missing XML root.");
        var menu = root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "listSetupMenu")
            ?? throw new InvalidDataException("Missing listSetupMenu.");
        return menu.Elements()
            .Select(e => new DenonSpeakerDiscoveryMenuItem(e.Name.LocalName, e.Value.Trim()))
            .ToArray();
    }

    internal static (string? Unit, string? Step) ParseDistanceMetadata(string xml)
    {
        var root = ParseXml(xml).Root ?? throw new InvalidDataException("Missing XML root.");
        var distances = root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "Distances")
            ?? throw new InvalidDataException("Missing Distances section.");
        return (
            distances.Elements().FirstOrDefault(e => e.Name.LocalName == "Unit")?.Value.Trim(),
            distances.Elements().FirstOrDefault(e => e.Name.LocalName == "Step")?.Value.Trim());
    }

    private static XDocument ParseXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_000_000
        });
        return XDocument.Load(reader, LoadOptions.None);
    }
}
