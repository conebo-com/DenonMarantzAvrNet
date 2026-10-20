using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using DenonMarantzAvrNet.Models;

namespace DenonMarantzAvrNet.Discovery;

/// <summary>Parses device-provided metadata without assuming model-specific capability tables.</summary>
internal static class DenonDeviceCapabilityReader
{
    private const int MaxXmlChars = 2 * 1024 * 1024;

    internal static DenonDeviceCapabilityDiscovery Parse(string xml, int port)
    {
        var root = SecureXml(xml).Root;
        if (root == null || !Is(root, "Device_Info"))
            throw new InvalidDataException("Expected a Device_Info XML document.");

        var advertised = new List<DenonAdvertisedCommand>();
        foreach (var container in root.Descendants().Where(e => Is(e, "Functions") || Is(e, "Commands")))
        {
            foreach (var command in container.Elements())
            {
                var flag = command.Value.Trim();
                if (flag != "1" && flag != "0") continue;
                var context = string.Join("/", container.AncestorsAndSelf()
                    .Reverse().Select(e => e.Name.LocalName));
                advertised.Add(new DenonAdvertisedCommand(command.Name.LocalName,
                    context, flag == "1", (string?)command.Attribute("ver"),
                    container.Name.LocalName));
                if (advertised.Count >= 2048) break;
            }
            if (advertised.Count >= 2048) break;
        }

        var setup = Child(Child(root, "DeviceCapabilities"), "Setup");
        var channelEntries = Child(Child(setup, "ChannelLevel"), "ChLists");
        var channels = (channelEntries == null
            ? Enumerable.Empty<XElement>()
            : channelEntries.Elements().Where(e => Is(e, "Ch")))
            .Select(ch => new DenonAdvertisedChannel(
                Text(ch, "Name") ?? "", Text(ch, "DispName"),
                Number(ch, "MinRange"), Number(ch, "MaxRange"),
                Number(ch, "DefaultValue"), Number(ch, "Step")))
            .Where(c => c.NativeCode.Length > 0)
            .Take(256).ToArray();

        // Only the Main Zone SurroundMode list is a grouped catalog. Do NOT
        // interpret the separate, partly inconsistent SoundMode/Genre values.
        var mainZone = root.Elements().Where(e => Is(e, "DeviceZoneCapabilities"))
            .FirstOrDefault(zone => Text(Child(zone, "Zone"), "No") == "0");
        var surroundList = Child(Child(mainZone, "SurroundMode"), "List");
        var soundModes = (surroundList == null
            ? Enumerable.Empty<XElement>()
            : surroundList.Elements().Where(e => Is(e, "Mode")))
            .Select(mode => new DenonAdvertisedSoundMode(
                Text(mode, "GroupName") ?? "Unspecified", Text(mode, "Name") ?? ""))
            .Where(mode => mode.Name.Length > 0)
            .Take(128).ToArray();

        var shortcuts = Child(Child(mainZone, "ShortcutControl"), "EntryList");
        // Category 5 identifies sound-mode shortcuts, but direct modes and
        // individual codecs also occur there. Keep only the four family keys.
        var families = (shortcuts == null ? Enumerable.Empty<XElement>() :
            shortcuts.Elements().Where(e => Is(e, "Shortcut")))
            .Where(e => Text(e, "Category") == "5")
            .Select(e => Text(e, "FuncName") ?? "")
            .Where(n => n == "MOVIE" || n == "MUSIC" || n == "GAME" || n == "PURE")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var volume = Child(mainZone, "Volume");
        var quick = Child(Child(mainZone, "Operation"), "QuickSelect");
        var tuner = Child(Child(mainZone, "Operation"), "TunerOperation");
        return new DenonDeviceCapabilityDiscovery(DateTimeOffset.UtcNow, port,
            advertised.ToArray(), channels, soundModes,
            Number(volume, "MaxValue"), Number(volume, "StepValue"),
            Integer(quick, "MaxQuickSelect"), Integer(tuner, "MaxPresets"), families);
    }

    internal static XDocument SecureXml(string xml)
    {
        if (string.IsNullOrEmpty(xml) || xml.Length > MaxXmlChars)
            throw new InvalidDataException("XML is empty or exceeds the 2 MiB limit.");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxXmlChars
        };
        using (var reader = XmlReader.Create(new StringReader(xml), settings))
            return XDocument.Load(reader);
    }

    internal static bool Is(XElement element, string name) =>
        string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(e => Is(e, name));
    private static string? Text(XElement? parent, string name) =>
        Child(parent, name)?.Value.Trim();
    private static double? Number(XElement? parent, string name) =>
        double.TryParse(Text(parent, name), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) ? value : (double?)null;
    private static int? Integer(XElement? parent, string name) =>
        int.TryParse(Text(parent, name), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) ? value : (int?)null;
}
