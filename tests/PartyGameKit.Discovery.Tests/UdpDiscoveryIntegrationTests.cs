using System.Net;
using System.Text;
using PartyGameKit.Core;
using PartyGameKit.Discovery.Lan;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.Lan;

namespace PartyGameKit.Discovery.Tests;

public sealed class UdpDiscoveryIntegrationTests
{
    [Fact]
    public async Task TwoAdvertisersAreDiscoveredAndPeriodicDuplicatesDoNotMultiplyRooms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var listener = new UdpLanDiscoveryListener(
            IPAddress.Loopback,
            discoveryPort: 0,
            cleanupInterval: TimeSpan.FromMilliseconds(25),
            registry: new DiscoveredSessionRegistry(TimeSpan.FromSeconds(2)));
        await listener.StartAsync(cancellationToken);
        await using var first = new UdpLanDiscoveryAdvertiser(
            Descriptor("room-a", "ROOMA", 5001),
            listener.BoundPort,
            TimeSpan.FromMilliseconds(25),
            [IPAddress.Loopback]);
        await using var second = new UdpLanDiscoveryAdvertiser(
            Descriptor("room-b", "ROOMB", 5002),
            listener.BoundPort,
            TimeSpan.FromMilliseconds(25),
            [IPAddress.Loopback]);
        await first.StartAsync(cancellationToken);
        await second.StartAsync(cancellationToken);

        var rooms = await WaitForSessionsAsync(listener, items => items.Count == 2, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

        Assert.Equal(2, rooms.Count);
        Assert.Equal(2, listener.Sessions.Count);
        Assert.Contains(listener.Sessions, item => item.RoomId == new RoomId("room-a"));
        Assert.Contains(listener.Sessions, item => item.RoomId == new RoomId("room-b"));
    }

    [Fact]
    public async Task AdvertiseDiscoverDescriptorRoundTripAndConnectWorksEndToEnd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(IPAddress.Loopback, 0),
            cancellationToken: cancellationToken);
        await using var hostEvents = host.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var descriptor = LanJoinDescriptor.Create(
            new RoomId("room-1"),
            new JoinCode("ROOM1"),
            "127.0.0.1",
            host.BoundPort,
            host.Path);
        descriptor = JoinDescriptorCodec.ParseText(JoinDescriptorCodec.SerializeText(descriptor));

        await using var listener = new UdpLanDiscoveryListener(IPAddress.Loopback, discoveryPort: 0);
        await listener.StartAsync(cancellationToken);
        await using var advertiser = new UdpLanDiscoveryAdvertiser(
            descriptor,
            listener.BoundPort,
            TimeSpan.FromMilliseconds(25),
            [IPAddress.Loopback]);
        await advertiser.StartAsync(cancellationToken);

        var sessions = await WaitForSessionsAsync(listener, items => items.Count == 1, cancellationToken);
        var discovered = Assert.Single(sessions).Descriptor;
        await using var client = await LanWebSocketClient.ConnectAsync(
            LanJoinDescriptor.GetEndpointUri(discovered),
            JoinRequest(discovered, "join-discovered"),
            cancellationToken: cancellationToken);

        Assert.True(await hostEvents.MoveNextAsync());
        var opened = Assert.IsType<TransportConnectionOpened>(hostEvents.Current);
        Assert.True(await hostEvents.MoveNextAsync());
        var handshake = Assert.IsType<TransportMessageReceived>(hostEvents.Current);
        Assert.Equal(opened.ConnectionId, handshake.ConnectionId);
        var parsed = ProtocolJson.Read<JoinRequestPayload>(
            Encoding.UTF8.GetString(handshake.Payload.Span),
            ProtocolMessageTypes.JoinRequest);
        Assert.True(parsed.IsSuccess);
        Assert.Equal(descriptor.RoomId, parsed.Message!.Payload.RoomId);
    }

    [Fact]
    public async Task ManualDescriptorConnectsWhenDiscoveryIsNotRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(IPAddress.Loopback, 0),
            cancellationToken: cancellationToken);
        await using var hostEvents = host.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var manuallySupplied = LanJoinDescriptor.Create(
            new RoomId("manual-room"),
            new JoinCode("MANUAL"),
            "127.0.0.1",
            host.BoundPort,
            host.Path);

        await using var client = await LanWebSocketClient.ConnectAsync(
            LanJoinDescriptor.GetEndpointUri(manuallySupplied),
            JoinRequest(manuallySupplied, "join-manual"),
            cancellationToken: cancellationToken);

        Assert.True(await hostEvents.MoveNextAsync());
        Assert.IsType<TransportConnectionOpened>(hostEvents.Current);
        Assert.True(await hostEvents.MoveNextAsync());
        var handshake = Assert.IsType<TransportMessageReceived>(hostEvents.Current);
        var parsed = ProtocolJson.Read<JoinRequestPayload>(
            Encoding.UTF8.GetString(handshake.Payload.Span),
            ProtocolMessageTypes.JoinRequest);
        Assert.True(parsed.IsSuccess);
        Assert.Equal(new JoinCode("MANUAL"), parsed.Message!.Payload.JoinCode);
    }

    private static async Task<IReadOnlyList<DiscoveredSession>> WaitForSessionsAsync(
        UdpLanDiscoveryListener listener,
        Func<IReadOnlyList<DiscoveredSession>, bool> predicate,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await foreach (var sessions in listener.ReadChangesAsync(timeout.Token))
        {
            if (predicate(sessions))
            {
                return sessions;
            }
        }

        throw new TimeoutException("Expected LAN discovery state was not observed.");
    }

    private static JoinDescriptor Descriptor(string roomId, string joinCode, int port) =>
        LanJoinDescriptor.Create(
            new RoomId(roomId),
            new JoinCode(joinCode),
            "127.0.0.1",
            port);

    private static string JoinRequest(JoinDescriptor descriptor, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinRequest,
            messageId,
            new JoinRequestPayload(
                descriptor.RoomId,
                descriptor.JoinCode,
                ClientRole.Player,
                new PlayerId($"player-{messageId}"))));
}
