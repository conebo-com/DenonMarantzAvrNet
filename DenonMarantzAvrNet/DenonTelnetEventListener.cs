using DenonMarantzAvrNet.Models;

namespace DenonMarantzAvrNet;

/// <summary>
/// Publishes events from the shared receiver Telnet session. Uses the same
/// connection as command clients, and checks connectivity for reconnects.
/// </summary>
public sealed class DenonTelnetEventListener : IAsyncDisposable
{
    private readonly TimeSpan _reconnectDelay;
    private readonly DenonTelnetSession _session;
    private CancellationTokenSource? _lifetime;
    private Task? _runTask;

    public DenonTelnetEventListener(string host, TimeSpan? reconnectDelay = null)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(host, nameof(host));
        Host = host.Trim().Trim('[', ']');
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(3);
        if (_reconnectDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reconnectDelay));
        _session = DenonTelnetSession.ForHost(Host);
    }

    public string Host { get; }
    public event Action<DenonTelnetEvent>? EventReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<Exception>? Error;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_lifetime is not null)
            throw new InvalidOperationException("Der Telnet-Event-Listener wurde bereits gestartet.");
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _session.AttachListener();
        _session.LineReceived += ForwardLine;
        _session.ConnectionChanged += ForwardConnection;
        _session.Error += ForwardError;
        _runTask = RunAsync(_lifetime.Token);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is null) return;
        _lifetime.Cancel();
        try { if (_runTask is not null) await _runTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally
        {
            _session.LineReceived -= ForwardLine;
            _session.ConnectionChanged -= ForwardConnection;
            _session.Error -= ForwardError;
            _session.DetachListener();
            _lifetime.Dispose();
            _lifetime = null;
        }
    }

    private void ForwardLine(DenonTelnetEvent value) => EventReceived?.Invoke(value);
    private void ForwardConnection(bool value) => ConnectionChanged?.Invoke(value);
    private void ForwardError(Exception value) => Error?.Invoke(value);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await _session.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) { Error?.Invoke(exception); }
            await Task.Delay(_reconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
