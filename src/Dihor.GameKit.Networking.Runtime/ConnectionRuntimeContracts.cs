using System.Text.Json;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;

namespace Dihor.GameKit.Networking.Runtime;

public sealed class ConnectionHostOptions
{
    public ConnectionHostOptions(
        TimeSpan peerTimeout,
        TimeSpan reconnectWindow,
        TimeSpan maintenanceInterval)
    {
        if (peerTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(peerTimeout), peerTimeout, "Peer timeout must be positive.");
        }

        if (reconnectWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reconnectWindow), reconnectWindow, "Reconnect window must be positive.");
        }

        if (maintenanceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maintenanceInterval),
                maintenanceInterval,
                "Maintenance interval must be positive.");
        }

        PeerTimeout = peerTimeout;
        ReconnectWindow = reconnectWindow;
        MaintenanceInterval = maintenanceInterval;
    }

    public TimeSpan PeerTimeout { get; }

    public TimeSpan ReconnectWindow { get; }

    public TimeSpan MaintenanceInterval { get; }

    public static ConnectionHostOptions Default { get; } = new(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(1));
}

public abstract record ConnectionHostEvent;

public sealed record ConnectionPeerConnected(
    PeerId PeerId,
    bool IsResume) : ConnectionHostEvent;

public sealed record ConnectionPeerDisconnected(
    PeerId PeerId,
    bool CanResume) : ConnectionHostEvent;

public sealed record ConnectionApplicationMessage : ConnectionHostEvent
{
    public ConnectionApplicationMessage(
        PeerId peerId,
        string messageId,
        string applicationType,
        JsonElement data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationType);
        if (data.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Application data cannot be undefined.", nameof(data));
        }

        PeerId = peerId;
        MessageId = messageId.Trim();
        ApplicationType = applicationType.Trim();
        Data = data.Clone();
    }

    public PeerId PeerId { get; }

    public string MessageId { get; }

    public string ApplicationType { get; }

    public JsonElement Data { get; }
}

public sealed record ConnectionResumeCredential(
    string Scope,
    PeerId PeerId,
    string ResumeToken)
{
    public ConnectionResumeCredential Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(PeerId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(ResumeToken);
        return new ConnectionResumeCredential(Scope.Trim(), PeerId, ResumeToken.Trim());
    }
}

public interface IConnectionResumeCredentialStore
{
    ValueTask<ConnectionResumeCredential?> ReadAsync(
        string scope,
        CancellationToken cancellationToken = default);

    ValueTask WriteAsync(
        ConnectionResumeCredential credential,
        CancellationToken cancellationToken = default);

    ValueTask ClearAsync(
        string scope,
        CancellationToken cancellationToken = default);
}

public sealed class MemoryConnectionResumeCredentialStore : IConnectionResumeCredentialStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ConnectionResumeCredential> _credentials = new(StringComparer.Ordinal);

    public ValueTask<ConnectionResumeCredential?> ReadAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _credentials.TryGetValue(scope.Trim(), out var credential);
            return ValueTask.FromResult(credential);
        }
    }

    public ValueTask WriteAsync(
        ConnectionResumeCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = credential.Normalize();
        lock (_gate)
        {
            _credentials[normalized.Scope] = normalized;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _credentials.Remove(scope.Trim());
        }

        return ValueTask.CompletedTask;
    }
}

public interface IConnectionTransportConnector
{
    Task<IMessageTransportClient> ConnectAsync(
        string handshake,
        bool isReconnect,
        CancellationToken cancellationToken = default);
}

public sealed class AutomaticTransportConnector : IConnectionTransportConnector
{
    private readonly AutomaticTransportSelector _selector;
    private readonly ConnectivityMode _mode;

    public AutomaticTransportConnector(
        AutomaticTransportSelector selector,
        ConnectivityMode mode = ConnectivityMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
        _mode = mode;
    }

    public async Task<IMessageTransportClient> ConnectAsync(
        string handshake,
        bool isReconnect,
        CancellationToken cancellationToken = default)
        => isReconnect
            ? await _selector.ReconnectAsync(handshake, _mode, cancellationToken).ConfigureAwait(false)
            : await _selector.ConnectAsync(handshake, _mode, cancellationToken).ConfigureAwait(false);
}

public sealed class ConnectionClientOptions
{
    public ConnectionClientOptions(
        TimeSpan heartbeatInterval,
        TimeSpan reconnectDelay)
    {
        if (heartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heartbeatInterval),
                heartbeatInterval,
                "Heartbeat interval must be positive.");
        }

        if (reconnectDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconnectDelay),
                reconnectDelay,
                "Reconnect delay cannot be negative.");
        }

        HeartbeatInterval = heartbeatInterval;
        ReconnectDelay = reconnectDelay;
    }

    public TimeSpan HeartbeatInterval { get; }

    public TimeSpan ReconnectDelay { get; }

    public static ConnectionClientOptions Default { get; } = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(1));
}

public enum ConnectionClientState
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Closing,
    Closed,
    Faulted,
}

public sealed record ConnectionClientApplicationMessage
{
    public ConnectionClientApplicationMessage(
        string messageId,
        string applicationType,
        JsonElement data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationType);
        if (data.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Application data cannot be undefined.", nameof(data));
        }

        MessageId = messageId.Trim();
        ApplicationType = applicationType.Trim();
        Data = data.Clone();
    }

    public string MessageId { get; }

    public string ApplicationType { get; }

    public JsonElement Data { get; }
}

public sealed class ConnectionRejectedException : Exception
{
    public ConnectionRejectedException(
        ConnectionRejectionCode code,
        string? reason = null)
        : base(string.IsNullOrWhiteSpace(reason)
            ? $"Connection was rejected with code '{code}'."
            : $"Connection was rejected with code '{code}': {reason}")
    {
        Code = code;
        Reason = reason;
    }

    public ConnectionRejectionCode Code { get; }

    public string? Reason { get; }
}
