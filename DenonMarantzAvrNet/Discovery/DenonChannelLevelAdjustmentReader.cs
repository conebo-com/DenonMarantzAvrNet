using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DenonMarantzAvrNet.Models;

namespace DenonMarantzAvrNet.Discovery;

/// <summary>Strict read-only decoder of the legacy GetChLevel response.</summary>
internal static class DenonChannelLevelAdjustmentReader
{
    internal static IReadOnlyList<DenonChannelLevelAdjustment> Parse(string xml)
    {
        var root = DenonDeviceCapabilityReader.SecureXml(xml).Root;
        if (root == null || !DenonDeviceCapabilityReader.Is(root, "rx"))
            throw new InvalidDataException("GetChLevel did not return an rx document.");

        var commands = root.Elements().Where(e => DenonDeviceCapabilityReader.Is(e, "cmd")).ToArray();
        if (commands.Length == 0 || commands.Any(e => e.Descendants().Any(x =>
            DenonDeviceCapabilityReader.Is(x, "error") || DenonDeviceCapabilityReader.Is(x, "fault"))))
            throw new InvalidDataException("GetChLevel returned no usable command data.");

        var result = new List<DenonChannelLevelAdjustment>();
        foreach (var cmd in commands)
        {
            foreach (var ch in cmd.Descendants().Where(e => DenonDeviceCapabilityReader.Is(e, "ch")).Take(128))
            {
                string? Get(string name) => ch.Elements().FirstOrDefault(e =>
                    DenonDeviceCapabilityReader.Is(e, name))?.Value.Trim();
                var code = Get("name");

                if (code == null || string.IsNullOrEmpty(code) || code.Length > 12 ||
                    !code.All(c => char.IsLetterOrDigit(c) || c == '_')) continue;
                result.Add(new DenonChannelLevelAdjustment(code,
                    Get("status"), Get("sptype"), Get("level"), Get("value")));
            }
        }
        return result;
    }
}
