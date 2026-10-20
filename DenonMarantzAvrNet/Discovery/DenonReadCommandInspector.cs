using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using DenonMarantzAvrNet.Models;

namespace DenonMarantzAvrNet.Discovery;

/// <summary>Classifies AppCommand reply shapes without exposing XML leaf values.</summary>
internal static class DenonReadCommandInspector
{
    internal static DenonReadCommandProbe Inspect(string command, string endpoint,
        int port, string xml)
    {
        DenonReadCommandProbe Result(string status, string note) =>
            new DenonReadCommandProbe(command, endpoint, port, status,
                Array.Empty<string>(), note);

        var doc = DenonDeviceCapabilityReader.SecureXml(xml);
        var root = doc.Root;
        if (root == null || !DenonDeviceCapabilityReader.Is(root, "rx"))
            return Result("Rejected", "Unexpected XML root.");
        if (root.Elements().Any(e => DenonDeviceCapabilityReader.Is(e, "error")))
            return Result("Rejected", "Receiver rejected the request.");

        var responses = root.Elements().Where(e => DenonDeviceCapabilityReader.Is(e, "cmd")).ToArray();
        if (responses.Length == 0) return Result("Empty", "No command result.");
        if (responses.Any(c => c.Descendants().Any(e =>
            DenonDeviceCapabilityReader.Is(e, "error") ||
            DenonDeviceCapabilityReader.Is(e, "fault"))))
            return Result("Rejected", "Command returned an error/fault element.");

        var fields = new List<string>();
        var payloadFound = false;
        foreach (var response in responses)
        {
            foreach (var node in response.Descendants().Take(128))
            {
                var path = string.Join("/", node.AncestorsAndSelf().Reverse()
                    .Select(e => e.Name.LocalName));
                var id = ((string?)node.Attribute("name") ?? "").Trim();
                if (id.Length > 0 && id.Length <= 64 && id.All(c =>
                    char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    path += "[@name=" + id + "]";
                fields.Add(path);
                if (!DenonDeviceCapabilityReader.Is(node, "name") &&
                    !DenonDeviceCapabilityReader.Is(node, "list") &&
                    !DenonDeviceCapabilityReader.Is(node, "status") &&
                    (node.HasElements || !string.IsNullOrWhiteSpace(node.Value) ||
                     node.HasAttributes))
                    payloadFound = true;
            }
        }
        return new DenonReadCommandProbe(command, endpoint, port,
            payloadFound ? "Received" : "Empty", fields.Distinct().Take(40).ToArray(),
            payloadFound ? "Response structure received; semantic values not yet verified." :
                "No recognizable payload; command support not confirmed.");

    }
}
