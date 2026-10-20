namespace DenonMarantzAvrNet;

/// <summary>
/// Correlates receiver status lines with an issued command by protocol family.
/// The Denon protocol has no request IDs. A matching status line indicates a
/// plausible response, not proof that the requested state change succeeded.
/// </summary>
internal static class DenonTelnetResponseMatcher
{
    internal static bool IsResponse(string command, string response)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(response))
            return false;

        var trimmed = response.Trim();
        var request = command.Trim().ToUpperInvariant();
        if (request == "CV?")
            return trimmed.StartsWith("CV", StringComparison.OrdinalIgnoreCase);

        if (request == "CVZRL")
            return trimmed.StartsWith("CV", StringComparison.OrdinalIgnoreCase);

        if (request.StartsWith("CV", StringComparison.Ordinal))
        {
            var codeEnd = request.IndexOfAny(new[] { '?', ' ' }, 2);
            var code = codeEnd >= 0 ? request.Substring(0, codeEnd) : request;
            return trimmed.StartsWith(code + " ", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, code, StringComparison.OrdinalIgnoreCase);
        }

        string prefix;
        if (request.StartsWith("PW", StringComparison.Ordinal)) prefix = "PW";
        else if (request.StartsWith("MV", StringComparison.Ordinal)) prefix = "MV";
        else if (request.StartsWith("MU", StringComparison.Ordinal)) prefix = "MU";
        else if (request.StartsWith("SI", StringComparison.Ordinal)) prefix = "SI";
        else if (request.StartsWith("MS", StringComparison.Ordinal)) prefix = "MS";
        else if (request.StartsWith("DC", StringComparison.Ordinal)) prefix = "DC";
        else if (request.StartsWith("Z2", StringComparison.Ordinal)) prefix = "Z2";
        else if (request.StartsWith("Z3", StringComparison.Ordinal)) prefix = "Z3";
        else if (request.StartsWith("SPPR", StringComparison.Ordinal)) prefix = "SPPR";
        else if (request.StartsWith("CVZRL", StringComparison.Ordinal)) prefix = "CV";
        else
        {
            // Unknown/raw commands cannot be correlated reliably. Never return
            // an unrelated unsolicited line and pretend it acknowledges a command.
            return trimmed.StartsWith(request.TrimEnd('?'), StringComparison.OrdinalIgnoreCase);
        }

        // MVMAX is a *different* status field, not the answer to MV?.
        if (prefix == "MV" && trimmed.StartsWith("MVMAX", StringComparison.OrdinalIgnoreCase))
            return false;
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
