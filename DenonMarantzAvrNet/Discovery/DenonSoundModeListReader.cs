using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DenonMarantzAvrNet.Models;

namespace DenonMarantzAvrNet.Discovery;

/// <summary>Parser for the optional multiline Denon OPSMLALL response.</summary>
internal static class DenonSoundModeListReader
{
    internal static IReadOnlyList<DenonSoundModeOption> Parse(IReadOnlyList<string> lines)
    {
        if (lines == null || lines.Count == 0 ||
            !string.Equals(lines[lines.Count - 1], "OPSMLALL END", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("OPSMLALL did not finish with END.");
        var result = new List<DenonSoundModeOption>();
        foreach (var line in lines.Take(256))
        {
            if (line == "OPSMLALL END") break;
            if (!line.StartsWith("OPSMLALL ", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line.Substring("OPSMLALL ".Length);
            if (value.Length < 7) continue;
            var family = value.Substring(0, 3).ToUpperInvariant();
            if (family != "MOV" && family != "MUS" && family != "GAM" && family != "PUR") continue;
            var slot = value.Substring(3, 2);
            if (!slot.All(char.IsDigit) || (value[5] != '0' && value[5] != '1')) continue;
            var label = value.Substring(6).Trim();
            if (label.Length == 0 || label.Length > 100 || label.Any(char.IsControl)) continue;
            result.Add(new DenonSoundModeOption(family, slot, value[5] == '1', label));
        }
        return result;
    }
}
