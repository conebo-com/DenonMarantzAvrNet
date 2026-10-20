using System;
using System.Collections.Generic;

namespace DenonMarantzAvrNet.Models;

/// <summary>
/// A diagnostic inventory of read-only HTTP endpoints. Field paths are XML element
/// names, NOT the contents of potentially private device settings.
/// </summary>
public sealed class DenonLegacyHttpDiscoveryReport
{
    public DenonLegacyHttpDiscoveryReport(DateTimeOffset capturedUtc,
        IReadOnlyList<DenonLegacyHttpProbe> probes)
    {
        CapturedUtc = capturedUtc;
        Probes = probes;
    }

    public DateTimeOffset CapturedUtc { get; }
    public IReadOnlyList<DenonLegacyHttpProbe> Probes { get; }
}

public sealed class DenonLegacyHttpProbe
{
    public DenonLegacyHttpProbe(string name, string method, string path, string result,
        string root, IReadOnlyList<string> elementPaths, IReadOnlyList<string> discoveredLinks,
        string note)
    {
        Name = name;
        Method = method;
        Path = path;
        Result = result;
        Root = root;
        ElementPaths = elementPaths;
        DiscoveredLinks = discoveredLinks;
        Note = note;
    }

    public string Name { get; }
    public string Method { get; }
    public string Path { get; }
    /// <summary>Received, Empty, Rejected, Unparseable, or Error.</summary>
    public string Result { get; }
    public string Root { get; }
    public IReadOnlyList<string> ElementPaths { get; }
    /// <summary>Paths referenced by received HTML (not automatically fetched).</summary>
    public IReadOnlyList<string> DiscoveredLinks { get; }
    public string Note { get; }
}
