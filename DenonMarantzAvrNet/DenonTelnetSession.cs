using DenonMarantzAvrNet.Logger;
using DenonMarantzAvrNet.Models;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace DenonMarantzAvrNet;

/// <summary>
/// One shared TCP session/reader per host for command clients and event listeners.
/// Denon's messages have no request IDs, so only one request is outstanding at a time.
/// Unmatched lines are published as status events, never as command responses.
/// </summary>
internal sealed class DenonTelnetSession
{
    private static readonly ConcurrentDictionary<string, DenonTelnetSession> Sessions =
        new(StringComparer.OrdinalIgnoreCase);

    internal static DenonTelnetSession ForHost(string host) => ForEndpoint(host, 23);

    // Internal endpoint override is only for deterministic localhost transport tests.
    internal static DenonTelnetSession ForEndpoint(string host, int port) =>
        Sessions.GetOrAdd(host + ":" + port, _ => new DenonTelnetSession(host, port));

    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly object _sync = new();
    private TcpClient? _client;
    private PendingRequest? _pending;

    private DenonTelnetSession(string host, int port) { _host = host; _port = port; }

    internal event Action<DenonTelnetEvent>? LineReceived;
    internal event Action<bool>? ConnectionChanged;
    internal event Action<Exception>? Error;
    private int _listenerCount;

    internal void AttachListener() => Interlocked.Increment(ref _listenerCount);

    internal void DetachListener()
    {
        if (Interlocked.Decrement(ref _listenerCount) > 0) return;
        TcpClient? client;
        lock (_sync) { client = _pending is null ? _client : null; }
        if (client is not null) DropConnection(client);
    }

    internal async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var connectedNow = false;
        try
        {
            lock (_sync)
            {
                if (_client is { Connected: true })
                    return;
            }

            var client = new TcpClient();
            using (cancellationToken.Register(() => client.Close()))
            {
                try { await client.ConnectAsync(_host, _port).ConfigureAwait(false); }
                catch { client.Dispose(); throw; }
            }
            lock (_sync) { _client = client; }
            connectedNow = true;
            _ = ReadLoopAsync(client);
        }
        finally { _connectGate.Release(); }
        // Do not invoke subscribers while holding the connection semaphore.
        if (connectedNow) ConnectionChanged?.Invoke(true);
    }

    internal async Task<IReadOnlyList<string>> ExecuteAsync(
        string command, string? terminatingLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutSource.CancelAfter(timeout);
            var token = timeoutSource.Token;
            await _commandGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await EnsureConnectedAsync(token).ConfigureAwait(false);
                var pending = new PendingRequest(command, terminatingLine);
                TcpClient client;
                lock (_sync)
                {
                    client = _client ?? throw new IOException("Telnet-Verbindung ist nicht verfügbar.");
                    _pending = pending;
                }
                using (token.Register(() =>
                {
                    pending.Fail(new OperationCanceledException(
                        "Telnet-Befehl abgebrochen oder Zeitlimit überschritten.", token));
                    DropConnection(client);
                }))
                {
                    var bytes = Encoding.ASCII.GetBytes(command + "\r");
                    try
                    {
                        await client.GetStream().WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                        await client.GetStream().FlushAsync(token).ConfigureAwait(false);
                        ReceiverLogger.Write("TELNET", "TX " + command);
                        return await pending.Completion.Task.ConfigureAwait(false);
                    }
                    catch
                    {
                        DropConnection(client);
                        throw;
                    }
                }
            }
            finally
            {
                lock (_sync) { _pending = null; }
                _commandGate.Release();
            }
        }
    }

    /// <summary>
    /// Sends a state-changing command without inventing an acknowledgement.
    /// Some receivers (including AVC-X6700H in field testing) switch their
    /// Movie/Music/Game family but never echo an MS status for the SET command.
    /// The caller must query and verify the resulting state separately.
    /// </summary>
    internal async Task SendWithoutResponseAsync(string command, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutSource.CancelAfter(timeout);
            var token = timeoutSource.Token;
            await _commandGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await EnsureConnectedAsync(token).ConfigureAwait(false);
                TcpClient client;
                lock (_sync)
                    client = _client ?? throw new IOException("Telnet-Verbindung ist nicht verfügbar.");
                using (token.Register(() => DropConnection(client)))
                {
                    try
                    {
                        var bytes = Encoding.ASCII.GetBytes(command + "\r");
                        await client.GetStream().WriteAsync(bytes, 0, bytes.Length, token)
                            .ConfigureAwait(false);
                        await client.GetStream().FlushAsync(token).ConfigureAwait(false);
                        ReceiverLogger.Write("TELNET", "TX " + command);
                    }
                    catch
                    {
                        DropConnection(client);
                        throw;
                    }
                }
            }
            finally { _commandGate.Release(); }
        }
    }

    private async Task ReadLoopAsync(TcpClient client)
    {
        Exception? failure = null;
        try
        {
            // Denon uses CR-only termination on some firmware versions.
            // StreamReader.ReadLineAsync can buffer the final CR while looking
            // for an LF; consume bytes explicitly so PWON\r is handled at once.
            var stream = client.GetStream();
            var buffer = new byte[2048];
            var line = new StringBuilder();
            while (true)
            {
                var count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (count == 0) throw new IOException("Receiver hat die Telnet-Verbindung geschlossen.");
                for (var index = 0; index < count; index++)
                {
                    var b = buffer[index];
                    if (b == 13 || b == 10)
                    {
                        if (line.Length == 0) continue;
                        DispatchLine(client, line.ToString().Trim());
                        line.Clear();
                    }
                    else
                    {
                        if (line.Length >= 16 * 1024)
                            throw new IOException("Ungültig lange Telnet-Statusmeldung.");
                        line.Append((char)b);
                    }
                }
            }
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            DropConnection(client);
            if (failure is not null && failure is not ObjectDisposedException)
                Error?.Invoke(failure);
        }
    }

    private void DispatchLine(TcpClient source, string line)
    {
        if (line.Length == 0) return;
        // A retired reader may still finish a buffered read after its socket
        // has been dropped on timeout. It must never satisfy a new request.
        lock (_sync)
        {
            if (!ReferenceEquals(_client, source)) return;
            _pending?.Receive(line);
        }
        ReceiverLogger.Write("TELNET", "RX " + line);
        // Events can contain unsolicited lines or status returned by commands.
        try { LineReceived?.Invoke(new DenonTelnetEvent(DateTimeOffset.UtcNow, line)); }
        catch (Exception exception) { Error?.Invoke(exception); }
    }

    private void DropConnection(TcpClient client)
    {
        PendingRequest? pending = null;
        var wasCurrent = false;
        lock (_sync)
        {
            if (ReferenceEquals(_client, client))
            {
                _client = null;
                pending = _pending;
                wasCurrent = true;
            }
        }
        client.Close();
        if (wasCurrent)
        {
            pending?.Fail(new IOException("Telnet-Verbindung unterbrochen."));
            ConnectionChanged?.Invoke(false);
        }
    }

    private sealed class PendingRequest
    {
        private readonly string _command;
        private readonly string? _terminator;
        private readonly List<string> _lines = new();
        internal readonly TaskCompletionSource<IReadOnlyList<string>> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal PendingRequest(string command, string? terminator)
        {
            _command = command;
            _terminator = terminator;
        }

        internal void Receive(string line)
        {
            if (Completion.Task.IsCompleted) return;
            if (line == "@RESTRICTED" || line == "@UNDEFINED")
            {
                Fail(new IOException("Telnet-Receiver antwortete: " + line));
                return;
            }
            if (!DenonTelnetResponseMatcher.IsResponse(_command, line)) return;
            lock (_lines)
            {
                _lines.Add(line);
                if (_terminator is null ||
                    string.Equals(line, _terminator, StringComparison.OrdinalIgnoreCase))
                    Completion.TrySetResult(_lines.ToArray());
            }
        }

        internal void Fail(Exception exception) => Completion.TrySetException(exception);
    }
}
