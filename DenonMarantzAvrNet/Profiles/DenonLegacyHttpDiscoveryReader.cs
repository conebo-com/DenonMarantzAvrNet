using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DenonMarantzAvrNet.Models;
using DenonMarantzAvrNet.Protocol;
using DenonMarantzAvrNet.Transport;

namespace DenonMarantzAvrNet.Profiles;

/// <summary>
/// Read-only protocol discovery for old Denon web interfaces. Each request is a
/// separately documented GET or AppCommand status query. HTML links are inventoried
/// without following them: an unknown GET URL could itself change a setting.
/// </summary>
internal static class DenonLegacyHttpDiscoveryReader
{
    private static readonly (string Name, string Method, string Path, string? Body)[] Requests =
    {
        ("Receiver web home", "GET", "/", null),
        ("Device info", "GET", DenonEndpoints.DeviceInfo, null),
        ("Legacy MainZone XML", "GET", "/goform/formMainZone_MainZoneXml.xml", null),
        ("Legacy MainZone status", "GET", DenonEndpoints.MainZoneStatus, null),
        ("Speaker AJAX menu (optional)", "GET", "/ajax/speakers/get_config?type=1", null),
        ("AppCommand surround status", "POST", DenonEndpoints.AppCommand,
            DenonAppCommand.CreateRequest("GetSurroundModeStatus")),
        ("AppCommand tone status", "POST", DenonEndpoints.AppCommand,
            DenonAppCommand.CreateRequest("GetToneControl")),
        ("AppCommand renamed sources", "POST", DenonEndpoints.AppCommand,
            DenonAppCommand.CreateRequest("GetRenameSource")),
        ("AppCommand0300 speaker matrix", "POST", DenonEndpoints.AppCommand0300,
            DenonAppCommand.CreateDetailedRequest(DenonAppCommand.GetActiveSpeaker, "activespall")),
        ("AppCommand0300 input signal", "POST", DenonEndpoints.AppCommand0300,
            DenonAppCommand.CreateDetailedRequest("GetInputSignal", "inputsigall"))
    };

    internal static async Task<DenonLegacyHttpDiscoveryReport> ReadAsync(
        string host, int port, DenonHttpTransport http, CancellationToken cancellationToken)
    {
        var results = new List<DenonLegacyHttpProbe>();
        foreach (var request in Requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // A missing optional endpoint must not stall the rest of the report.
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var content = request.Method == "GET"
                        ? await http.GetStringAsync(host, port, request.Path, timeout.Token)
                            .ConfigureAwait(false)
                        : await http.PostXmlAsync(host, port, request.Path,
                            request.Body!, timeout.Token).ConfigureAwait(false);
                    results.Add(DenonLegacyHttpResponseInspector.Inspect(
                        request.Name, request.Method, request.Path, content));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is HttpRequestException ||
                    exception is OperationCanceledException || exception is IOException ||
                    exception is XmlException)
                {
                    // Never equate an individual HTTP failure with absent hardware capability.
                    results.Add(new DenonLegacyHttpProbe(request.Name, request.Method,
                        request.Path, "Error", "", Array.Empty<string>(), Array.Empty<string>(),
                        exception is OperationCanceledException ? "Request timed out" :
                        exception.GetType().Name));
                }
            }
        }
        return new DenonLegacyHttpDiscoveryReport(DateTimeOffset.UtcNow, results);
    }
}

internal static class DenonLegacyHttpResponseInspector
{
    private static readonly Regex HtmlReferences = new Regex(
        "(?:href|src)\\s*=\\s*['\"]([^'\"#]+)['\"]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static DenonLegacyHttpProbe Inspect(
        string name, string method, string path, string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return Probe("Empty", "", Array.Empty<string>(), Array.Empty<string>(), "No response body");
        if (response.Length > 1024 * 1024)
            return Probe("Unparseable", "", Array.Empty<string>(), Array.Empty<string>(), "Body above 1 MiB inspection limit");

        // HTML is a web UI, not an XML capability catalogue. Never crawl its links.
        if (response.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 ||
            response.IndexOf("<!doctype html", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var links = HtmlReferences.Matches(response).Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(link => link.StartsWith("/", StringComparison.Ordinal) ||
                               (!link.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                                !link.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)))
                .Where(link => link.IndexOf("speaker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               link.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               link.IndexOf("goform", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               link.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                .Where(link => link.Length <= 200 && link.IndexOf('?') < 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
            return Probe("Received", "HTML", Array.Empty<string>(), links,
                "Web page; links inventoried, not fetched");
        }

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            using (var reader = XmlReader.Create(new StringReader(response), settings))
            {
                var document = XDocument.Load(reader);
                var root = document.Root;
                if (root == null)
                    return Probe("Empty", "", Array.Empty<string>(), Array.Empty<string>(), "XML without root");
                // Keep names only: values may include MACs, user-assigned names,
                // IP addresses, streaming titles or other private information.
                var paths = root.DescendantsAndSelf()
                    .Select(element => string.Join("/", element.AncestorsAndSelf()
                        .Reverse().Select(node => node.Name.LocalName)))
                    .Distinct(StringComparer.Ordinal).Take(80).ToArray();
                var rejected = root.Name.LocalName.Equals("rx", StringComparison.OrdinalIgnoreCase) &&
                    !DenonXmlParser.HasAppCommandData(response);
                return Probe(rejected ? "Rejected" : "Received", root.Name.LocalName,
                    paths, Array.Empty<string>(), rejected ? "Empty/negative AppCommand response" :
                    "XML structure only; values intentionally omitted");
            }
        }
        catch (XmlException)
        {
            return Probe("Unparseable", "", Array.Empty<string>(), Array.Empty<string>(),
                "Non-XML body (no values included)");
        }

        DenonLegacyHttpProbe Probe(string result, string root,
            IReadOnlyList<string> fields, IReadOnlyList<string> links, string note) =>
            new DenonLegacyHttpProbe(name, method, path, result, root, fields, links, note);
    }
}
