using DenonMarantzAvrNet.Transport;

namespace DenonMarantzAvrNet.Profiles;

/// <summary>Infrastructure shared by receiver-specific HTTP feature providers.</summary>
internal sealed class DenonProfileContext(string host, DenonHttpTransport httpTransport)
{
    internal string Host { get; } = host;
    internal DenonHttpTransport HttpTransport { get; } = httpTransport;
}
