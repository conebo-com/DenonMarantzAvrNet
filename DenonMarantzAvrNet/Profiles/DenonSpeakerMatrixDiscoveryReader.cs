using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DenonMarantzAvrNet.Models;

namespace DenonMarantzAvrNet.Profiles;

/// <summary>
/// Interprets the read-only GetActiveSpeaker/AppCommand0300 reply. Keeps native
/// identifiers and control values; deliberately makes no calibration mapping.
/// </summary>
internal static class DenonSpeakerMatrixDiscoveryReader
{
    private const int MaxResponseChars = 1024 * 1024;
    private static readonly Regex NativeIdentifier = new Regex(
        "^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);

    internal static DenonSpeakerMatrixDiscovery Parse(string? xml, int? port)
    {
        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(xml))
            return new DenonSpeakerMatrixDiscovery(now, null, "NoResponse",
                Array.Empty<DenonSpeakerMatrixSlot>());

        if (xml!.Length > MaxResponseChars)
            throw new InvalidDataException("GetActiveSpeaker response exceeds 1 MiB.");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxResponseChars
        };
        XDocument doc;
        using (var reader = XmlReader.Create(new StringReader(xml), settings))
            doc = XDocument.Load(reader);

        var root = doc.Root;
        if (root == null || !Is(root, "rx"))
            throw new InvalidDataException("Expected an AppCommand0300 <rx> response.");

        var commands = root.Elements().Where(e => Is(e, "cmd")).ToArray();
        if (commands.Length == 0)
            return new DenonSpeakerMatrixDiscovery(now, port,
                root.Elements().Any(e => Is(e, "error")) ? "Rejected" : "Empty",
                Array.Empty<DenonSpeakerMatrixSlot>());

        var command = commands.FirstOrDefault(cmd =>
            cmd.Elements().Any(e => Is(e, "name") &&
                string.Equals(e.Value.Trim(), "GetActiveSpeaker", StringComparison.OrdinalIgnoreCase)));
        // Some firmware omits <name>. In that case only accept a single command
        // containing the known activesp* parameter family.
        if (command == null && commands.Length == 1 &&
            !commands[0].Elements().Any(e => Is(e, "name")) &&
            commands[0].Descendants().Any(e => Is(e, "param") &&
                (string?)e.Attribute("name") is string id &&
                id.StartsWith("activesp", StringComparison.OrdinalIgnoreCase)))
            command = commands[0];

        if (command == null)
            return new DenonSpeakerMatrixDiscovery(now, port, "Empty",
                Array.Empty<DenonSpeakerMatrixSlot>());

        var list = command.Elements().FirstOrDefault(e => Is(e, "list"));
        var slots = new List<DenonSpeakerMatrixSlot>();
        if (list != null)
        {
            foreach (var parameter in list.Elements().Where(e => Is(e, "param")))
            {
                var id = ((string?)parameter.Attribute("name") ?? "").Trim();
                if (!NativeIdentifier.IsMatch(id) ||
                    !id.StartsWith("activesp", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? label = parameter.Value.Trim();
                if (label.Length == 0) label = null;
                var control = ((string?)parameter.Attribute("control"))?.Trim();
                if (string.IsNullOrEmpty(control)) control = null;
                slots.Add(new DenonSpeakerMatrixSlot(id, label, control));
            }
        }
        return new DenonSpeakerMatrixDiscovery(now, port,
            slots.Count > 0 ? "Received" : "Empty", slots.ToArray());
    }

    private static bool Is(XElement element, string name) =>
        string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
}
