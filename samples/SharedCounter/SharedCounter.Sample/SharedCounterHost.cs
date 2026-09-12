using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.Lan;

namespace PartyGameKit.Sample.SharedCounter;

public sealed record SharedCounterPlayerView(
    [property: JsonPropertyName("playerId")] string PlayerId,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("connected")] bool Connected);

public sealed record SharedCounterPublicState(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("players")] IReadOnlyList<SharedCounterPlayerView> Players);

public sealed record SharedCounterPlayerState(
    [property: JsonPropertyName("yourCount")] int YourCount);

public sealed record SharedCounterPlayerProjection(
    [property: JsonPropertyName("publicState")] SharedCounterPublicState PublicState,
    [property: JsonPropertyName("privateState")] SharedCounterPlayerState PrivateState);

public sealed class SharedCounterHost : IAsyncDisposable
{
    public const string IncrementCommandType = "sample.counter.increment";

    private static readonly RoomId CounterRoomId = new("shared-counter-room");
    private static readonly JoinCode CounterJoinCode = new("COUNT1");
    private static readonly AuthorityId CounterAuthorityId = new("shared-counter-host");

    private readonly IGameTransport _transport;
    private readonly RoomSession _session;
    private readonly AuthoritativeSnapshotPublisher<SharedCounterPublicState, SharedCounterPlayerState> _publisher;
    private readonly SessionContinuityCoordinator<SharedCounterPublicState, SharedCounterPlayerState> _continuity;
    private readonly Dictionary<PlayerId, int> _counts = new();
    private readonly CancellationTokenSource _stopSource = new();
    private Task _eventLoop = Task.CompletedTask;
    private int _disposed;

    private SharedCounterHost(
        IGameTransport transport,
        RoomSession session,
        AuthoritativeSnapshotPublisher<SharedCounterPublicState, SharedCounterPlayerState> publisher,
        SessionContinuityCoordinator<SharedCounterPublicState, SharedCounterPlayerState> continuity,
        JoinDescriptor joinDescriptor)
    {
        _transport = transport;
        _session = session;
        _publisher = publisher;
        _continuity = continuity;
        JoinDescriptor = joinDescriptor;
    }

    public JoinDescriptor JoinDescriptor { get; }

    public Uri ClientUri => new(JoinDescriptor.Endpoint);

    public RoomSession Session => _session;

    public static RoomId RoomId => CounterRoomId;

    public static JoinCode JoinCode => CounterJoinCode;

    public static async Task<SharedCounterHost> StartAsync(
        string advertisedHost,
        IPAddress? bindAddress = null,
        int port = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(advertisedHost);
        var transport = await LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(
                bindAddress ?? IPAddress.Any,
                port,
                handshakeTimeout: TimeSpan.FromSeconds(5),
                keepAliveInterval: TimeSpan.FromSeconds(10)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            var descriptor = new JoinDescriptor(
                CounterRoomId,
                CounterJoinCode,
                "lan-websocket",
                transport.CreateClientUri(advertisedHost).AbsoluteUri,
                ProtocolVersions.Current);
            return StartWithTransport(transport, descriptor);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static SharedCounterHost StartWithTransport(
        IGameTransport transport,
        JoinDescriptor joinDescriptor,
        SessionContinuityOptions? continuityOptions = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(joinDescriptor);
        if (joinDescriptor.RoomId != CounterRoomId || joinDescriptor.JoinCode != CounterJoinCode)
        {
            throw new ArgumentException(
                $"Shared Counter requires room '{CounterRoomId.Value}' and join code '{CounterJoinCode.Value}'.",
                nameof(joinDescriptor));
        }

        if (joinDescriptor.ProtocolVersion != ProtocolVersions.Current)
        {
            throw new ArgumentException(
                $"Shared Counter requires protocol version {ProtocolVersions.Current}.",
                nameof(joinDescriptor));
        }

        var session = new RoomSession(CounterRoomId, CounterJoinCode, playerCapacity: 8, CounterAuthorityId);
        var publisher = new AuthoritativeSnapshotPublisher<SharedCounterPublicState, SharedCounterPlayerState>(CounterRoomId);
        var continuity = new SessionContinuityCoordinator<SharedCounterPublicState, SharedCounterPlayerState>(
            session,
            publisher,
            continuityOptions ?? new SessionContinuityOptions(
                hostTimeout: TimeSpan.FromSeconds(30),
                clientTimeout: TimeSpan.FromSeconds(30),
                reconnectWindow: TimeSpan.FromMinutes(2)));
        var host = new SharedCounterHost(transport, session, publisher, continuity, joinDescriptor);
        host._eventLoop = host.RunEventLoopAsync(host._stopSource.Token);
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopSource.Cancel();
        try
        {
            await _transport.StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await _eventLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _stopSource.Dispose();
        }
    }

    private async Task RunEventLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var transportEvent in _transport.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (transportEvent)
            {
                case TransportMessageReceived received:
                    await HandleMessageAsync(received.ConnectionId, received.Payload, cancellationToken).ConfigureAwait(false);
                    break;
                case TransportConnectionClosed closed:
                    var disconnected = _continuity.MarkDisconnected(closed.ConnectionId);
                    if (disconnected.Status == DisconnectClientStatus.Disconnected)
                    {
                        await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
                    }
                    break;
                case TransportFaulted faulted:
                    Console.Error.WriteLine($"Transport fault: {faulted.Error.Code}: {faulted.Error.Message}");
                    break;
            }
        }
    }

    private async Task HandleMessageAsync(
        ConnectionId connectionId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(payload.Span);
        string? type;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            type = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("type", out var typeProperty) &&
                typeProperty.ValueKind == JsonValueKind.String
                    ? typeProperty.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return;
        }

        switch (type)
        {
            case ProtocolMessageTypes.JoinRequest:
                await HandleJoinAsync(connectionId, json, cancellationToken).ConfigureAwait(false);
                break;
            case ProtocolMessageTypes.RejoinRequest:
                await HandleRejoinAsync(connectionId, json, cancellationToken).ConfigureAwait(false);
                break;
            case ProtocolMessageTypes.Heartbeat:
                HandleHeartbeat(connectionId, json);
                break;
            case ProtocolMessageTypes.Leave:
                await HandleLeaveAsync(connectionId, json, cancellationToken).ConfigureAwait(false);
                break;
            case IncrementCommandType:
                await HandleIncrementAsync(connectionId, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleJoinAsync(ConnectionId connectionId, string json, CancellationToken cancellationToken)
    {
        var read = ProtocolJson.Read<JoinRequestPayload>(json, ProtocolMessageTypes.JoinRequest);
        if (!read.IsSuccess)
        {
            return;
        }

        var request = read.Message!.Payload;
        if (request.RoomId != _session.RoomId || request.JoinCode != _session.JoinCode)
        {
            await SendJoinRejectedAsync(
                connectionId,
                request.JoinCode,
                JoinRejectionCode.RoomNotFound,
                "Room or join code does not match this sample host.",
                read.Message.MessageId,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Role == ClientRole.Player)
        {
            if (request.PlayerId is not { } playerId)
            {
                await SendJoinRejectedAsync(
                    connectionId,
                    request.JoinCode,
                    JoinRejectionCode.ResumeRejected,
                    "Player role requires a stable playerId.",
                    read.Message.MessageId,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var join = _continuity.JoinPlayer(playerId, connectionId);
            if (!join.IsAccepted)
            {
                await SendJoinRejectedAsync(
                    connectionId,
                    request.JoinCode,
                    MapJoinRejection(join.Status),
                    $"Join rejected: {join.Status}.",
                    read.Message.MessageId,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            _counts.TryAdd(playerId, 0);
            var accepted = PartyGameKitMessages.Create(
                ProtocolMessageTypes.JoinAccepted,
                $"join-accepted-{Guid.NewGuid():N}",
                new JoinAcceptedPayload(
                    _session.RoomId,
                    connectionId,
                    ClientRole.Player,
                    playerId,
                    _session.AuthorityId,
                    join.ReconnectToken),
                read.Message.MessageId);
            await SendProtocolAsync(connectionId, accepted, cancellationToken).ConfigureAwait(false);
            await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Role == ClientRole.SharedScreen)
        {
            var connected = _continuity.ConnectClient(connectionId, ClientRole.SharedScreen);
            if (!connected.IsAccepted)
            {
                await SendJoinRejectedAsync(
                    connectionId,
                    request.JoinCode,
                    connected.Status == ConnectClientStatus.RoomClosed
                        ? JoinRejectionCode.RoomClosed
                        : JoinRejectionCode.ResumeRejected,
                    $"Shared screen rejected: {connected.Status}.",
                    read.Message.MessageId,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var accepted = PartyGameKitMessages.Create(
                ProtocolMessageTypes.JoinAccepted,
                $"join-accepted-{Guid.NewGuid():N}",
                new JoinAcceptedPayload(
                    _session.RoomId,
                    connectionId,
                    ClientRole.SharedScreen,
                    PlayerId: null,
                    AuthorityId: _session.AuthorityId),
                read.Message.MessageId);
            await SendProtocolAsync(connectionId, accepted, cancellationToken).ConfigureAwait(false);
            await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendJoinRejectedAsync(
            connectionId,
            request.JoinCode,
            JoinRejectionCode.ResumeRejected,
            "This sample only accepts player and shared-screen browser roles.",
            read.Message.MessageId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleRejoinAsync(ConnectionId connectionId, string json, CancellationToken cancellationToken)
    {
        var read = ProtocolJson.Read<RejoinRequestPayload>(json, ProtocolMessageTypes.RejoinRequest);
        if (!read.IsSuccess)
        {
            return;
        }

        var request = read.Message!.Payload;
        if (request.RoomId != _session.RoomId)
        {
            await SendRejoinRejectedAsync(
                connectionId,
                request.RoomId,
                RejoinRejectionCodes.InvalidResumeIdentity,
                "Room does not match this sample host.",
                read.Message.MessageId,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var resumed = _continuity.RejoinPlayer(request.PlayerId, connectionId, request.ReconnectToken);
        if (!resumed.IsAccepted)
        {
            await SendRejoinRejectedAsync(
                connectionId,
                request.RoomId,
                MapRejoinRejection(resumed.Status),
                $"Rejoin rejected: {resumed.Status}.",
                read.Message.MessageId,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var accepted = PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinAccepted,
            $"rejoin-accepted-{Guid.NewGuid():N}",
            new RejoinAcceptedPayload(
                _session.RoomId,
                request.PlayerId,
                connectionId,
                _session.AuthorityId),
            read.Message.MessageId);
        await SendProtocolAsync(connectionId, accepted, cancellationToken).ConfigureAwait(false);
        await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private void HandleHeartbeat(ConnectionId connectionId, string json)
    {
        var read = ProtocolJson.Read<HeartbeatPayload>(json, ProtocolMessageTypes.Heartbeat);
        if (read.IsSuccess && read.Message!.Payload.RoomId == _session.RoomId)
        {
            _continuity.RecordHeartbeat(connectionId);
        }
    }

    private async Task HandleLeaveAsync(ConnectionId connectionId, string json, CancellationToken cancellationToken)
    {
        var read = ProtocolJson.Read<LeavePayload>(json, ProtocolMessageTypes.Leave);
        if (!read.IsSuccess || read.Message!.Payload.RoomId != _session.RoomId)
        {
            return;
        }

        var leave = read.Message.Payload;
        var authenticatedClient = _session.FindClient(connectionId);
        if (authenticatedClient is null || leave.ConnectionId != connectionId)
        {
            return;
        }

        if (authenticatedClient.Role == ClientRole.Player)
        {
            if (authenticatedClient.PlayerId is not { } authenticatedPlayerId ||
                leave.PlayerId != authenticatedPlayerId)
            {
                return;
            }

            if (_continuity.LeavePlayer(authenticatedPlayerId) == LeavePlayerStatus.Left)
            {
                _counts.Remove(authenticatedPlayerId);
            }
        }
        else
        {
            if (leave.PlayerId is not null)
            {
                return;
            }

            _continuity.MarkDisconnected(connectionId);
        }

        await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        await _transport.DisconnectAsync(connectionId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleIncrementAsync(ConnectionId connectionId, CancellationToken cancellationToken)
    {
        var client = _session.FindClient(connectionId);
        if (client?.Role != ClientRole.Player || client.PlayerId is not { } playerId)
        {
            return;
        }

        _counts[playerId] = _counts.GetValueOrDefault(playerId) + 1;
        await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishCurrentStateAsync(CancellationToken cancellationToken)
    {
        var players = _session.Players
            .OrderBy(player => player.PlayerId.Value, StringComparer.Ordinal)
            .ToArray();
        var publicState = new SharedCounterPublicState(
            players.Sum(player => _counts.GetValueOrDefault(player.PlayerId)),
            players.Select(player => new SharedCounterPlayerView(
                player.PlayerId.Value,
                _counts.GetValueOrDefault(player.PlayerId),
                player.Presence == PlayerPresence.Connected)).ToArray());
        var privateState = players.ToDictionary(
            player => player.PlayerId,
            player => new SharedCounterPlayerState(_counts.GetValueOrDefault(player.PlayerId)));
        var published = _publisher.Publish(_session.AuthorityId, publicState, privateState);

        var publicMessage = PartyGameKitMessages.Create(
            ProtocolMessageTypes.StateSnapshot,
            $"snapshot-{published.PublicSnapshot.Sequence.Value}-public",
            new StateSnapshotPayload<SharedCounterPublicState>(
                published.PublicSnapshot.RoomId,
                published.PublicSnapshot.AuthorityId,
                published.PublicSnapshot.Sequence,
                published.PublicSnapshot.Target,
                published.PublicSnapshot.Projection.State));
        var publicBytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(publicMessage));

        foreach (var client in _session.Clients.ToArray())
        {
            await TrySendAsync(client.ConnectionId, publicBytes, cancellationToken).ConfigureAwait(false);
        }

        foreach (var player in players.Where(player => player.ConnectionId is not null))
        {
            if (!published.PlayerSnapshots.TryGetValue(player.PlayerId, out var snapshot) ||
                player.ConnectionId is not { } connectionId)
            {
                continue;
            }

            var projection = new SharedCounterPlayerProjection(
                snapshot.Projection.PublicState,
                snapshot.Projection.PrivateState);
            var privateMessage = PartyGameKitMessages.Create(
                ProtocolMessageTypes.StateSnapshot,
                $"snapshot-{snapshot.Sequence.Value}-player-{player.PlayerId.Value}",
                new StateSnapshotPayload<SharedCounterPlayerProjection>(
                    snapshot.RoomId,
                    snapshot.AuthorityId,
                    snapshot.Sequence,
                    snapshot.Target,
                    projection));
            await TrySendAsync(
                connectionId,
                Encoding.UTF8.GetBytes(ProtocolJson.Serialize(privateMessage)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendJoinRejectedAsync(
        ConnectionId connectionId,
        JoinCode joinCode,
        JoinRejectionCode code,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinRejected,
            $"join-rejected-{Guid.NewGuid():N}",
            new JoinRejectedPayload(joinCode, code, reason),
            correlationId);
        await SendProtocolAsync(connectionId, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRejoinRejectedAsync(
        ConnectionId connectionId,
        RoomId roomId,
        string code,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinRejected,
            $"rejoin-rejected-{Guid.NewGuid():N}",
            new RejoinRejectedPayload(roomId, code, reason),
            correlationId);
        await SendProtocolAsync(connectionId, message, cancellationToken).ConfigureAwait(false);
    }

    private Task SendProtocolAsync<TPayload>(
        ConnectionId connectionId,
        ProtocolEnvelope<TPayload> message,
        CancellationToken cancellationToken) =>
        TrySendAsync(connectionId, Encoding.UTF8.GetBytes(ProtocolJson.Serialize(message)), cancellationToken);

    private async Task TrySendAsync(
        ConnectionId connectionId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _transport.SendAsync(connectionId, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (PartyGameTransportException exception)
            when (exception.Error.Code is TransportErrorCode.ConnectionNotFound or TransportErrorCode.DeliveryFailed)
        {
        }
    }

    private static JoinRejectionCode MapJoinRejection(JoinPlayerStatus status) => status switch
    {
        JoinPlayerStatus.RoomClosed => JoinRejectionCode.RoomClosed,
        JoinPlayerStatus.RoomFull => JoinRejectionCode.RoomFull,
        _ => JoinRejectionCode.ResumeRejected,
    };

    private static string MapRejoinRejection(ResumePlayerStatus status) => status switch
    {
        ResumePlayerStatus.RoomClosed => RejoinRejectionCodes.RoomClosed,
        ResumePlayerStatus.ReconnectWindowExpired => RejoinRejectionCodes.ReconnectWindowExpired,
        ResumePlayerStatus.ConnectionAlreadyInUse => RejoinRejectionCodes.ConnectionAlreadyInUse,
        _ => RejoinRejectionCodes.InvalidResumeIdentity,
    };
}
