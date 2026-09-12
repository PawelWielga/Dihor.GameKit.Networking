using PartyGameKit.Core;

namespace PartyGameKit.Transport.Abstractions;

public enum TransportCloseReason
{
    Normal,
    RemoteClosed,
    Timeout,
    Replaced,
    TransportStopped,
    Faulted,
}

public enum TransportErrorCode
{
    ConnectionNotFound,
    ConnectionAlreadyOpen,
    TransportClosed,
    DeliveryFailed,
}

public sealed record TransportError(
    TransportErrorCode Code,
    string Message,
    ConnectionId? ConnectionId = null);

public sealed class PartyGameTransportException : Exception
{
    public PartyGameTransportException(TransportError error)
        : base(error.Message)
    {
        Error = error;
    }

    public TransportError Error { get; }
}

public abstract record TransportEvent;

public sealed record TransportConnectionOpened(ConnectionId ConnectionId) : TransportEvent;

public sealed record TransportConnectionClosed(
    ConnectionId ConnectionId,
    TransportCloseReason Reason) : TransportEvent;

public sealed record TransportMessageReceived(
    ConnectionId ConnectionId,
    ReadOnlyMemory<byte> Payload) : TransportEvent;

public sealed record TransportFaulted(TransportError Error) : TransportEvent;

public interface IGameTransport : IAsyncDisposable
{
    IAsyncEnumerable<TransportEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default);

    ValueTask SendAsync(
        ConnectionId connectionId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    ValueTask BroadcastAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    ValueTask DisconnectAsync(
        ConnectionId connectionId,
        TransportCloseReason reason = TransportCloseReason.Normal,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
