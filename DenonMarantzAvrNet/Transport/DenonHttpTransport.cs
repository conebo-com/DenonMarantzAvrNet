using DenonMarantzAvrNet.Logger;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;

namespace DenonMarantzAvrNet.Transport;

internal sealed class DenonHttpTransport : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _localDeviceHttpsClient;

    internal DenonHttpTransport(TimeSpan timeout)
        : this(CreateDefaultHandler(timeout), timeout)
    {
    }

    internal DenonHttpTransport(HttpMessageHandler handler, TimeSpan timeout)
    {
        NetFrameworkCompat.ThrowIfNull(handler, nameof(handler));

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _httpClient = CreateClient(handler, timeout);
        _localDeviceHttpsClient = CreateClient(
            CreateLocalDeviceHttpsHandler(timeout),
            timeout);
    }

    internal Task<string> GetStringAsync(
        string host,
        int port,
        string pathAndQuery,
        CancellationToken cancellationToken) =>
        GetStringAsync(
            host,
            Uri.UriSchemeHttp,
            port,
            pathAndQuery,
            allowUntrustedServerCertificate: false,
            cancellationToken);

    internal Task<string> GetStringAsync(
        string host,
        string scheme,
        int port,
        string pathAndQuery,
        CancellationToken cancellationToken) =>
        GetStringAsync(
            host,
            scheme,
            port,
            pathAndQuery,
            allowUntrustedServerCertificate: false,
            cancellationToken);

    internal async Task<string> GetStringAsync(
        string host,
        string scheme,
        int port,
        string pathAndQuery,
        bool allowUntrustedServerCertificate,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildUri(host, scheme, port, pathAndQuery);
        var client = SelectClient(requestUri, allowUntrustedServerCertificate);

        ReceiverLogger.Write("HTTP", $"GET {requestUri}");

        try
        {
            using var response = await client.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            ReceiverLogger.Write("HTTP", $"GET {requestUri} -> {(int)response.StatusCode}");

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            ReceiverLogger.Write("HTTP", $"GET {requestUri} response body:{Environment.NewLine}{body}");

            return body;
            // <-----------
        }
        catch (Exception exception)
        {
            ReceiverLogger.WriteException("HTTP", $"GET {requestUri}", exception);
            throw;
            // <-----------
        }
    }

    internal async Task<string> PostXmlAsync(
        string host,
        int port,
        string path,
        string xml,
        CancellationToken cancellationToken)
    {
        NetFrameworkCompat.ThrowIfNull(xml, nameof(xml));

        var requestUri = BuildUri(host, Uri.UriSchemeHttp, port, path);

        ReceiverLogger.Write("HTTP", $"POST {requestUri} request body:{Environment.NewLine}{xml}");

        try
        {
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/xml")
            {
                CharSet = "utf-8"
            };

            using var response = await _httpClient.PostAsync(
                requestUri,
                content,
                cancellationToken).ConfigureAwait(false);

            ReceiverLogger.Write("HTTP", $"POST {requestUri} -> {(int)response.StatusCode}");

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            ReceiverLogger.Write("HTTP", $"POST {requestUri} response body:{Environment.NewLine}{body}");

            return body;
            // <-----------
        }
        catch (Exception exception)
        {
            ReceiverLogger.WriteException("HTTP", $"POST {requestUri}", exception);
            throw;
            // <-----------
        }
    }

    internal async Task<string> PostFormAsync(string host, int port, string path,
        IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        NetFrameworkCompat.ThrowIfNull(form, nameof(form));
        var requestUri = BuildUri(host, Uri.UriSchemeHttp, port, path);
        ReceiverLogger.Write("HTTP", $"POST form {requestUri} ({form.Count} fields)");
        using (var content = new FormUrlEncodedContent(form))
        using (var response = await _httpClient.PostAsync(requestUri, content, cancellationToken)
            .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _localDeviceHttpsClient.Dispose();
    }

    private static HttpClient CreateClient(
        HttpMessageHandler handler,
        TimeSpan timeout)
    {
        var client = new HttpClient(handler)
        {
            Timeout = timeout
        };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DenonAvrNet", "0.1"));

        return client;
    }

    private HttpClient SelectClient(
        Uri requestUri,
        bool allowUntrustedServerCertificate)
    {
        if (!allowUntrustedServerCertificate)
        {
            return _httpClient;
            // <-----------
        }

        if (!requestUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Untrusted server certificates can only be enabled for HTTPS requests.");
        }

        return _localDeviceHttpsClient;
        // <-----------
    }

    private static HttpMessageHandler CreateDefaultHandler(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
            // <-----------
        }

        return new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        // <-----------
    }

    private static HttpMessageHandler CreateLocalDeviceHttpsHandler(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
            // <-----------
        }

        return new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            // Some Denon receivers expose their local setup API through HTTPS
            // with a certificate that is not trusted by the operating-system
            // certificate store. This handler is used only when a receiver
            // profile explicitly opts in to accepting that device certificate.
            ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
        };
        // <-----------
    }

    private static Uri BuildUri(
        string host,
        string scheme,
        int port,
        string pathAndQuery)
    {
        if (!scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Das URI-Schema muss 'http' oder 'https' sein.",
                nameof(scheme));
        }

        if (!pathAndQuery.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Ein Denon-Befehlspfad muss mit '/' beginnen.", nameof(pathAndQuery));
        }

        var builder = new UriBuilder(scheme, host, port);
        var queryStart = pathAndQuery.IndexOf('?');

        if (queryStart < 0)
        {
            builder.Path = pathAndQuery;
        }
        else
        {
            builder.Path = pathAndQuery.Substring(0, queryStart);
            builder.Query = pathAndQuery.Substring(queryStart + 1);
        }

        return builder.Uri;
    }
}
