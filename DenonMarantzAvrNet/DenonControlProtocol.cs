namespace DenonMarantzAvrNet;

/// <summary>Specifies the transport used for a control command.</summary>
public enum DenonControlProtocol
{
    /// <summary>
    /// Uses HTTP after a successful <see cref="DenonAvrClient.InitializeAsync"/> call;
    /// otherwise uses Telnet. If an absolute HTTP control command (power, volume value,
    /// mute, input) fails with an HTTP error or an HTTP timeout, Telnet is used as a fallback.
    /// Relative step commands (volume up/down) are never repeated through Telnet, because
    /// the receiver may already have applied the step.
    /// </summary>
    Auto,
    /// <summary>Uses the receiver's HTTP/XML control interface.</summary>
    Http,

    /// <summary>Uses the receiver's TCP control interface (normally port 23).</summary>
    Telnet
}
