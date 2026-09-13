using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.SignalR;
using PartyGameKit.Transport.SignalR.Server;

namespace PartyGameKit.Transport.Tests;

public sealed class SignalRRelayIntegrationTests
{
    [Fact]
    public async Task MultipleChannelsTargetBroadcastAndDisconnectStayIsolated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await RelayTestServer.StartAsync(cancellationToken);

        var channelA = new ChannelId("channel-a");
        var channelB = new ChannelId("channel-b");
        await using var transportA = await SignalRRelayTransport.StartAsync(
            new SignalRRelayOptions(server.Endpoint, channelA),
            cancellationToken);
        await using var transportB = await SignalRRelayTransport.StartAsync(
            new SignalRRelayOptions(server.Endpoint, channelB),
            cancellationToken);
        await using var eventsA = transportA
            .ReadEventsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using var eventsB = transportB
            .ReadEventsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await using var clientA1 = await SignalRRelayClient.ConnectAsync(
            new SignalRRelayOptions(server.Endpoint, channelA),
            CreateConnectHandshake(new PeerId("peer-a1"), "connect-a1"),
            cancellationToken);
        var connectionA1 = await ReadHandshakeAsync(eventsA, clientA1.ConnectionId);

        await using var clientA2 = await SignalRRelayClient.ConnectAsync(
            new SignalRRelayOptions(server.Endpoint, channelA),
            CreateConnectHandshake(new PeerId("peer-a2"), "connect-a2"),
            cancellationToken);
        var connectionA2 = await ReadHandshakeAsync(eventsA, clientA2.ConnectionId);

        await using var clientB1 = await SignalRRelayClient.ConnectAsync(
            new SignalRRelayOptions(server.Endpoint, channelB),
            CreateConnectHandshake(new PeerId("peer-b1"), "connect-b1"),
            cancellationToken);
        await ReadHandshakeAsync(eventsB, clientB1.ConnectionId);

        var peerToListener = ApplicationBytes("test.peer-to-listener", new { value = 1 }, "app-1");
        await clientA1.SendAsync(peerToListener, cancellationToken);
        var received = await ReadEventAsync<TransportMessageReceived>(eventsA);
        Assert.Equal(connectionA1, received.ConnectionId);
        Assert.Equal(peerToListener, received.Payload.ToArray());

        var targeted = ApplicationBytes("test.targeted", new { target = "a1" }, "app-2");
        await transportA.SendAsync(connectionA1, targeted, cancellationToken);
        var targetedMessage = await clientA1.ReceiveAsync(cancellationToken);
        Assert.False(targetedMessage.IsClose);
        Assert.Equal(targeted, targetedMessage.Payload.ToArray());

        var broadcast = ApplicationBytes("test.broadcast", new { scope = "channel-a" }, "app-3");
        await transportA.BroadcastAsync(broadcast, cancellationToken);
        var broadcastA1 = await clientA1.ReceiveAsync(cancellationToken);
        var broadcastA2 = await clientA2.ReceiveAsync(cancellationToken);
        Assert.Equal(broadcast, broadcastA1.Payload.ToArray());
        Assert.Equal(broadcast, broadcastA2.Payload.ToArray());

        using (var isolationTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await clientB1.ReceiveAsync(isolationTimeout.Token);
            });
        }

        await transportA.DisconnectAsync(
            connectionA2,
            TransportCloseReason.Replaced,
            cancellationToken);
        var clientClose = await clientA2.ReceiveAsync(cancellationToken);
        Assert.True(clientClose.IsClose);
        Assert.Equal("replaced", clientClose.CloseDescription);
        var listenerClose = await ReadEventAsync<TransportConnectionClosed>(eventsA);
        Assert.Equal(connectionA2, listenerClose.ConnectionId);
        Assert.Equal(TransportCloseReason.Replaced, listenerClose.Reason);

        Assert.Equal(1, transportA.ConnectionCount);
        Assert.Equal(1, transportB.ConnectionCount);
    }

    [Fact]
    public async Task ProtocolV1HandshakeIsRejectedBeforeConnectionOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await RelayTestServer.StartAsync(cancellationToken);
        var channel = new ChannelId("protocol-mismatch");
        await using var transport = await SignalRRelayTransport.StartAsync(
            new SignalRRelayOptions(server.Endpoint, channel),
            cancellationToken);

        const string protocolV1Handshake =
            "{\"protocolVersion\":1,\"type\":\"connection.connect.request\",\"messageId\":\"legacy-connect\",\"payload\":{\"peerId\":\"peer-legacy\"}}";
        await using var client = await SignalRRelayClient.ConnectAsync(
            new SignalRRelayOptions(server.Endpoint, channel),
            protocolV1Handshake,
            cancellationToken);

        var closed = await client.ReceiveAsync(cancellationToken);
        Assert.True(closed.IsClose);
        Assert.Equal("protocol-version-mismatch", closed.CloseDescription);
        await WaitUntilAsync(() => transport.ConnectionCount == 0, cancellationToken);
        Assert.Equal(0, transport.ConnectionCount);
    }

    [Fact]
    public async Task ReplacementRelayConnectionResumesSameNeutralPeer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await RelayTestServer.StartAsync(cancellationToken);
        var channel = new ChannelId("resume-channel");
        await using var transport = await SignalRRelayTransport.StartAsync(
            new SignalRRelayOptions(server.Endpoint, channel),
            cancellationToken);
        await using var events = transport
            .ReadEventsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var continuity = new ConnectionContinuityCoordinator(
            new ConnectionContinuityOptions(
                peerTimeout: TimeSpan.FromSeconds(5),
                reconnectWindow: TimeSpan.FromSeconds(5)),
            resumeTokenFactory: () => "resume-token-signalr");
        var peerId = new PeerId("stable-peer");

        ConnectionId firstConnection;
        string resumeToken;
        await using (var firstClient = await SignalRRelayClient.ConnectAsync(
                         new SignalRRelayOptions(server.Endpoint, channel),
                         CreateConnectHandshake(peerId, "connect-1"),
                         cancellationToken))
        {
            firstConnection = await ReadHandshakeAsync(events, firstClient.ConnectionId);
            var registration = continuity.Register(peerId, firstConnection);
            Assert.True(registration.IsConnected);
            resumeToken = Assert.IsType<string>(registration.ResumeToken);

            var accepted = PartyGameKitMessages.Create(
                ProtocolMessageTypes.ConnectAccepted,
                "accepted-1",
                new ConnectAcceptedPayload(firstConnection, peerId, resumeToken),
                "connect-1");
            await transport.SendAsync(
                firstConnection,
                Utf8(ProtocolJson.Serialize(accepted)),
                cancellationToken);
            var response = await firstClient.ReceiveAsync(cancellationToken);
            Assert.False(response.IsClose);
        }

        var disconnected = await ReadEventAsync<TransportConnectionClosed>(events);
        Assert.Equal(firstConnection, disconnected.ConnectionId);
        var marked = continuity.MarkDisconnected(firstConnection);
        Assert.Equal(DisconnectPeerStatus.Disconnected, marked.Status);

        await using var resumedClient = await SignalRRelayClient.ConnectAsync(
            new SignalRRelayOptions(server.Endpoint, channel),
            CreateResumeHandshake(peerId, resumeToken, "resume-1"),
            cancellationToken);
        var secondConnection = await ReadHandshakeAsync(events, resumedClient.ConnectionId);
        Assert.NotEqual(firstConnection, secondConnection);

        var resume = continuity.Resume(peerId, resumeToken, secondConnection);
        Assert.True(resume.IsResumed);
        Assert.Equal(1, continuity.PeerCount);
        Assert.Equal(secondConnection, continuity.GetPresence(peerId)?.ConnectionId);

        var resumeAccepted = PartyGameKitMessages.Create(
            ProtocolMessageTypes.ResumeAccepted,
            "resume-accepted-1",
            new ResumeAcceptedPayload(secondConnection, peerId, resume.ResumeToken),
            "resume-1");
        await transport.SendAsync(
            secondConnection,
            Utf8(ProtocolJson.Serialize(resumeAccepted)),
            cancellationToken);
        var resumedResponse = await resumedClient.ReceiveAsync(cancellationToken);
        Assert.False(resumedResponse.IsClose);
    }

    private static async Task<ConnectionId> ReadHandshakeAsync(
        IAsyncEnumerator<TransportEvent> events,
        ConnectionId expectedConnectionId)
    {
        var opened = await ReadEventAsync<TransportConnectionOpened>(events);
        Assert.Equal(expectedConnectionId, opened.ConnectionId);
        var handshake = await ReadEventAsync<TransportMessageReceived>(events);
        Assert.Equal(expectedConnectionId, handshake.ConnectionId);
        return opened.ConnectionId;
    }

    private static async Task<TEvent> ReadEventAsync<TEvent>(
        IAsyncEnumerator<TransportEvent> events)
        where TEvent : TransportEvent
    {
        while (await events.MoveNextAsync())
        {
            if (events.Current is TEvent typed)
            {
                return typed;
            }
        }

        throw new InvalidOperationException($"Transport event stream ended before {typeof(TEvent).Name} was observed.");
    }

    private static string CreateConnectHandshake(PeerId peerId, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.ConnectRequest,
            messageId,
            new ConnectRequestPayload(peerId)));

    private static string CreateResumeHandshake(
        PeerId peerId,
        string resumeToken,
        string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.ResumeRequest,
            messageId,
            new ResumeRequestPayload(peerId, resumeToken)));

    private static byte[] ApplicationBytes(string applicationType, object data, string messageId)
    {
        var payload = new ApplicationMessagePayload(
            applicationType,
            JsonSerializer.SerializeToElement(data));
        return Utf8(ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.ApplicationMessage,
            messageId,
            payload)));
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(20, cancellationToken);
        }
    }

    private sealed class RelayTestServer : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private RelayTestServer(WebApplication application, Uri endpoint)
        {
            _application = application;
            Endpoint = endpoint;
        }

        public Uri Endpoint { get; }

        public static async Task<RelayTestServer> StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(RelayTestServer).Assembly.FullName,
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
            });
            builder.Services.AddPartyGameKitSignalRRelay();

            var application = builder.Build();
            application.MapPartyGameKitSignalRRelay();
            await application.StartAsync(cancellationToken);

            var server = application.Services.GetRequiredService<IServer>();
            var boundAddress = server.Features
                .Get<IServerAddressesFeature>()?
                .Addresses
                .FirstOrDefault();
            if (boundAddress is null || !Uri.TryCreate(boundAddress, UriKind.Absolute, out var baseUri))
            {
                await application.DisposeAsync();
                throw new InvalidOperationException("Test relay server did not expose a bound address.");
            }

            var endpoint = new Uri(baseUri, "/partygamekit-relay");
            return new RelayTestServer(application, endpoint);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _application.StopAsync(CancellationToken.None);
            }
            finally
            {
                await _application.DisposeAsync();
            }
        }
    }
}
