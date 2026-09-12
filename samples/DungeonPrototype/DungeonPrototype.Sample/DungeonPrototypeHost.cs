using System.Net;
using System.Text;
using System.Text.Json;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.Lan;

namespace PartyGameKit.Sample.DungeonPrototype;

public sealed class DungeonPrototypeHost : IAsyncDisposable
{
    public const string SelectCharacterCommandType = "sample.dungeon.select-character";
    public const string MoveCommandType = "sample.dungeon.move";
    public const string AttackCommandType = "sample.dungeon.attack";
    public const string EndTurnCommandType = "sample.dungeon.end-turn";

    private readonly LanWebSocketTransport _transport;
    private readonly RoomSession _session;
    private readonly AuthoritativeSnapshotPublisher<DungeonPublicState, DungeonPrivateState> _publisher;
    private readonly SessionContinuityCoordinator<DungeonPublicState, DungeonPrivateState> _continuity;
    private readonly DungeonGame _game = new();
    private readonly CancellationTokenSource _stopSource = new();
    private Task _eventLoop = Task.CompletedTask;
    private int _disposed;

    private DungeonPrototypeHost(
        LanWebSocketTransport transport,
        RoomSession session,
        AuthoritativeSnapshotPublisher<DungeonPublicState, DungeonPrivateState> publisher,
        SessionContinuityCoordinator<DungeonPublicState, DungeonPrivateState> continuity,
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

    public static async Task<DungeonPrototypeHost> StartAsync(
        string advertisedHost,
        IPAddress? bindAddress = null,
        int port = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(advertisedHost);
        var roomId = new RoomId("dungeon-prototype-room");
        var joinCode = new JoinCode("DUNGE1");
        var authorityId = new AuthorityId("dungeon-prototype-host");
        var transport = await LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(
                bindAddress ?? IPAddress.Any,
                port,
                handshakeTimeout: TimeSpan.FromSeconds(5),
                keepAliveInterval: TimeSpan.FromSeconds(10)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            var session = new RoomSession(roomId, joinCode, playerCapacity: 4, authorityId);
            var publisher = new AuthoritativeSnapshotPublisher<DungeonPublicState, DungeonPrivateState>(roomId);
            var continuity = new SessionContinuityCoordinator<DungeonPublicState, DungeonPrivateState>(
                session,
                publisher,
                new SessionContinuityOptions(
                    hostTimeout: TimeSpan.FromSeconds(30),
                    clientTimeout: TimeSpan.FromSeconds(30),
                    reconnectWindow: TimeSpan.FromMinutes(2)));
            var descriptor = new JoinDescriptor(
                roomId,
                joinCode,
                "lan-websocket",
                transport.CreateClientUri(advertisedHost).AbsoluteUri,
                ProtocolVersions.Current);
            var host = new DungeonPrototypeHost(transport, session, publisher, continuity, descriptor);
            host._eventLoop = host.RunEventLoopAsync(host._stopSource.Token);
            return host;
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
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
                    Console.Error.WriteLine($"LAN transport fault: {faulted.Error.Code}: {faulted.Error.Message}");
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
        using var document = TryParse(json);
        if (document is null)
        {
            return;
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeProperty) ||
            typeProperty.ValueKind != JsonValueKind.String)
        {
            return;
        }

        switch (typeProperty.GetString())
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
            case SelectCharacterCommandType:
                await HandleSelectCharacterAsync(connectionId, root, cancellationToken).ConfigureAwait(false);
                break;
            case MoveCommandType:
                await HandleMoveAsync(connectionId, root, cancellationToken).ConfigureAwait(false);
                break;
            case AttackCommandType:
                await HandlePlayerActionAsync(connectionId, _game.Attack, cancellationToken).ConfigureAwait(false);
                break;
            case EndTurnCommandType:
                await HandlePlayerActionAsync(connectionId, _game.EndTurn, cancellationToken).ConfigureAwait(false);
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
                "Room or join code does not match this dungeon host.",
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

            _game.EnsurePlayer(playerId);
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
                "Room does not match this dungeon host.",
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
        if (leave.PlayerId is { } playerId)
        {
            if (_continuity.LeavePlayer(playerId) == LeavePlayerStatus.Left)
            {
                _game.RemovePlayer(playerId);
            }
        }
        else
        {
            _continuity.MarkDisconnected(connectionId);
        }

        await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        await _transport.DisconnectAsync(connectionId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleSelectCharacterAsync(
        ConnectionId connectionId,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var playerId = PlayerFor(connectionId);
        if (playerId is null ||
            !root.TryGetProperty("character", out var characterProperty) ||
            characterProperty.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var character = characterProperty.GetString();
        if (character is not null && _game.SelectCharacter(playerId.Value, character))
        {
            await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleMoveAsync(
        ConnectionId connectionId,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var playerId = PlayerFor(connectionId);
        if (playerId is null ||
            !root.TryGetProperty("dx", out var deltaXProperty) ||
            !root.TryGetProperty("dy", out var deltaYProperty) ||
            !deltaXProperty.TryGetInt32(out var deltaX) ||
            !deltaYProperty.TryGetInt32(out var deltaY))
        {
            return;
        }

        if (_game.Move(playerId.Value, deltaX, deltaY))
        {
            await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandlePlayerActionAsync(
        ConnectionId connectionId,
        Func<PlayerId, bool> action,
        CancellationToken cancellationToken)
    {
        var playerId = PlayerFor(connectionId);
        if (playerId is not null && action(playerId.Value))
        {
            await PublishCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private PlayerId? PlayerFor(ConnectionId connectionId)
    {
        var client = _session.FindClient(connectionId);
        return client?.Role == ClientRole.Player ? client.PlayerId : null;
    }

    private async Task PublishCurrentStateAsync(CancellationToken cancellationToken)
    {
        var publicState = _game.CreatePublicState(
            playerId => _session.FindPlayer(playerId)?.Presence == PlayerPresence.Connected);
        var privateStates = _game.CreatePrivateStates();
        var published = _publisher.Publish(_session.AuthorityId, publicState, privateStates);

        var publicMessage = PartyGameKitMessages.Create(
            ProtocolMessageTypes.StateSnapshot,
            $"snapshot-{published.PublicSnapshot.Sequence.Value}-public",
            new StateSnapshotPayload<DungeonPublicState>(
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

        foreach (var player in _session.Players.Where(player => player.ConnectionId is not null).ToArray())
        {
            if (!published.PlayerSnapshots.TryGetValue(player.PlayerId, out var snapshot) ||
                player.ConnectionId is not { } connectionId)
            {
                continue;
            }

            var projection = new DungeonPlayerProjection(
                snapshot.Projection.PublicState,
                snapshot.Projection.PrivateState);
            var privateMessage = PartyGameKitMessages.Create(
                ProtocolMessageTypes.StateSnapshot,
                $"snapshot-{snapshot.Sequence.Value}-player-{player.PlayerId.Value}",
                new StateSnapshotPayload<DungeonPlayerProjection>(
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

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
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
