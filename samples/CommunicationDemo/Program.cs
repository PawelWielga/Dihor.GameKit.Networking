using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using PartyGameKit.Core;
using PartyGameKit.Discovery.Lan;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.Lan;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var cancellationToken = timeout.Token;

var nextConnection = 0;
await using var transport = await LanWebSocketTransport.StartAsync(
    new LanWebSocketHostOptions(IPAddress.Loopback),
    () => $"demo-connection-{Interlocked.Increment(ref nextConnection)}",
    cancellationToken);

var descriptor = new ConnectionDescriptor(
    "lan-websocket",
    transport.CreateClientUri(IPAddress.Loopback.ToString()).AbsoluteUri,
    ProtocolVersions.Current,
    new ChannelId("communication-demo"));
var serializedDescriptor = ConnectionDescriptorCodec.SerializeUri(descriptor);
var directDescriptor = ConnectionDescriptorCodec.ParseUri(serializedDescriptor);
Ensure(directDescriptor == descriptor, "Serialized connection descriptor did not round-trip.");

var continuity = new ConnectionContinuityCoordinator(
    new ConnectionContinuityOptions(
        peerTimeout: TimeSpan.FromSeconds(5),
        reconnectWindow: TimeSpan.FromSeconds(5)),
    resumeTokenFactory: () => $"resume-{Guid.NewGuid():N}");

var receivedApplications = Channel.CreateUnbounded<ReceivedApplication>();
var hostLoop = RunHostAsync(transport, continuity, receivedApplications.Writer, cancellationToken);

await VerifyDiscoveryAsync(directDescriptor, cancellationToken);

var peerA = new PeerId("peer-a");
var peerB = new PeerId("peer-b");
var endpoint = new Uri(directDescriptor.Endpoint);

await using var clientB = await LanWebSocketClient.ConnectAsync(
    endpoint,
    CreateConnectHandshake(peerB, "connect-b"),
    cancellationToken: cancellationToken);
var acceptedB = await ReadProtocolAsync<ConnectAcceptedPayload>(
    clientB,
    ProtocolMessageTypes.ConnectAccepted,
    cancellationToken);
Ensure(acceptedB.Payload.PeerId == peerB, "Peer B identity was not preserved.");

var clientA = await LanWebSocketClient.ConnectAsync(
    endpoint,
    CreateConnectHandshake(peerA, "connect-a"),
    cancellationToken: cancellationToken);
var acceptedA = await ReadProtocolAsync<ConnectAcceptedPayload>(
    clientA,
    ProtocolMessageTypes.ConnectAccepted,
    cancellationToken);
Ensure(acceptedA.Payload.PeerId == peerA, "Peer A identity was not preserved.");
Ensure(!string.IsNullOrWhiteSpace(acceptedA.Payload.ResumeToken), "Peer A did not receive a resume token.");

await clientA.SendAsync(
    CreateApplicationBytes("demo.from-peer", new { sender = "peer-a", value = 1 }, "app-a-1"),
    cancellationToken);
var fromPeer = await receivedApplications.Reader.ReadAsync(cancellationToken);
Ensure(fromPeer.ConnectionId == acceptedA.Payload.ConnectionId, "Host observed the application message on the wrong connection.");
Ensure(fromPeer.ApplicationType == "demo.from-peer", "Host did not preserve the consumer application type.");

await transport.SendAsync(
    acceptedA.Payload.ConnectionId,
    CreateApplicationBytes("demo.targeted", new { recipient = "peer-a" }, "targeted-1"),
    cancellationToken);
var targeted = await ReadProtocolAsync<ApplicationMessagePayload>(
    clientA,
    ProtocolMessageTypes.ApplicationMessage,
    cancellationToken);
Ensure(targeted.Payload.ApplicationType == "demo.targeted", "Targeted delivery failed.");

await transport.BroadcastAsync(
    CreateApplicationBytes("demo.broadcast", new { audience = "all" }, "broadcast-1"),
    cancellationToken);
var broadcastA = await ReadProtocolAsync<ApplicationMessagePayload>(
    clientA,
    ProtocolMessageTypes.ApplicationMessage,
    cancellationToken);
var broadcastB = await ReadProtocolAsync<ApplicationMessagePayload>(
    clientB,
    ProtocolMessageTypes.ApplicationMessage,
    cancellationToken);
Ensure(broadcastA.Payload.ApplicationType == "demo.broadcast", "Peer A did not receive the broadcast.");
Ensure(broadcastB.Payload.ApplicationType == "demo.broadcast", "Peer B did not receive the broadcast.");

var originalConnectionA = acceptedA.Payload.ConnectionId;
var resumeToken = acceptedA.Payload.ResumeToken!;
await clientA.DisposeAsync();
await WaitUntilAsync(
    () => continuity.GetPresence(peerA)?.State == PeerConnectionState.AwaitingResume,
    cancellationToken);

await using var resumedClientA = await LanWebSocketClient.ConnectAsync(
    endpoint,
    CreateResumeHandshake(peerA, resumeToken, "resume-a"),
    cancellationToken: cancellationToken);
var resumedA = await ReadProtocolAsync<ResumeAcceptedPayload>(
    resumedClientA,
    ProtocolMessageTypes.ResumeAccepted,
    cancellationToken);

Ensure(resumedA.Payload.PeerId == peerA, "Resume changed the stable peer identity.");
Ensure(resumedA.Payload.ConnectionId != originalConnectionA, "Resume reused the old transient connection ID.");
Ensure(continuity.PeerCount == 2, "Resume created a duplicate logical peer.");
Ensure(continuity.GetPresence(peerA)?.ConnectionId == resumedA.Payload.ConnectionId, "Continuity coordinator did not bind the replacement connection.");

await resumedClientA.SendAsync(
    CreateApplicationBytes("demo.after-resume", new { sender = "peer-a" }, "app-a-2"),
    cancellationToken);
var afterResume = await receivedApplications.Reader.ReadAsync(cancellationToken);
Ensure(afterResume.ConnectionId == resumedA.Payload.ConnectionId, "Application traffic did not continue on the replacement connection.");

Console.WriteLine("PartyGameKit neutral communication demo passed.");
Console.WriteLine($"Direct descriptor: {serializedDescriptor}");
Console.WriteLine("Verified: LAN host, discovery, two generic peers, opaque application messages, targeted delivery, broadcast and resume.");

await transport.StopAsync(cancellationToken);
await hostLoop;

static async Task RunHostAsync(
    IMessageTransport transport,
    ConnectionContinuityCoordinator continuity,
    ChannelWriter<ReceivedApplication> applications,
    CancellationToken cancellationToken)
{
    try
    {
        await foreach (var transportEvent in transport.ReadEventsAsync(cancellationToken))
        {
            switch (transportEvent)
            {
                case TransportConnectionClosed closed:
                    continuity.MarkDisconnected(closed.ConnectionId);
                    break;

                case TransportMessageReceived received:
                    await HandleHostMessageAsync(
                        transport,
                        continuity,
                        applications,
                        received,
                        cancellationToken);
                    break;
            }
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
    }
    finally
    {
        applications.TryComplete();
    }
}

static async Task HandleHostMessageAsync(
    IMessageTransport transport,
    ConnectionContinuityCoordinator continuity,
    ChannelWriter<ReceivedApplication> applications,
    TransportMessageReceived received,
    CancellationToken cancellationToken)
{
    var json = Encoding.UTF8.GetString(received.Payload.Span);
    using var document = JsonDocument.Parse(json);
    var type = document.RootElement.GetProperty("type").GetString();

    switch (type)
    {
        case ProtocolMessageTypes.ConnectRequest:
        {
            var request = RequireMessage<ConnectRequestPayload>(json, ProtocolMessageTypes.ConnectRequest);
            var peerId = request.Payload.PeerId
                ?? throw new InvalidOperationException("Communication demo requires a stable peer ID for resume verification.");
            var registration = continuity.Register(peerId, received.ConnectionId);
            Ensure(registration.IsConnected, $"Unable to register {peerId}: {registration.Status}.");
            var accepted = PartyGameKitMessages.Create(
                ProtocolMessageTypes.ConnectAccepted,
                $"accepted-{received.ConnectionId.Value}",
                new ConnectAcceptedPayload(received.ConnectionId, peerId, registration.ResumeToken),
                request.MessageId);
            await transport.SendAsync(received.ConnectionId, Utf8(ProtocolJson.Serialize(accepted)), cancellationToken);
            break;
        }

        case ProtocolMessageTypes.ResumeRequest:
        {
            var request = RequireMessage<ResumeRequestPayload>(json, ProtocolMessageTypes.ResumeRequest);
            var resume = continuity.Resume(request.Payload.PeerId, request.Payload.ResumeToken, received.ConnectionId);
            Ensure(resume.IsResumed, $"Unable to resume {request.Payload.PeerId}: {resume.Status}.");
            var accepted = PartyGameKitMessages.Create(
                ProtocolMessageTypes.ResumeAccepted,
                $"resumed-{received.ConnectionId.Value}",
                new ResumeAcceptedPayload(received.ConnectionId, request.Payload.PeerId, resume.ResumeToken),
                request.MessageId);
            await transport.SendAsync(received.ConnectionId, Utf8(ProtocolJson.Serialize(accepted)), cancellationToken);
            break;
        }

        case ProtocolMessageTypes.Heartbeat:
            continuity.RecordHeartbeat(received.ConnectionId);
            break;

        case ProtocolMessageTypes.ApplicationMessage:
        {
            var message = RequireMessage<ApplicationMessagePayload>(json, ProtocolMessageTypes.ApplicationMessage);
            applications.TryWrite(new ReceivedApplication(
                received.ConnectionId,
                message.Payload.ApplicationType,
                message.Payload.Data));
            break;
        }
    }
}

static async Task VerifyDiscoveryAsync(
    ConnectionDescriptor descriptor,
    CancellationToken cancellationToken)
{
    await using var listener = new UdpLanDiscoveryListener(
        bindAddress: IPAddress.Loopback,
        discoveryPort: 0,
        cleanupInterval: TimeSpan.FromMilliseconds(50));
    await listener.StartAsync(cancellationToken);

    await using var advertiser = new UdpLanDiscoveryAdvertiser(
        descriptor,
        discoveryPort: listener.BoundPort,
        interval: TimeSpan.FromMilliseconds(25),
        targetAddresses: new[] { IPAddress.Loopback });
    await advertiser.StartAsync(cancellationToken);

    await WaitUntilAsync(
        () => listener.Endpoints.Any(endpoint => endpoint.Descriptor == descriptor),
        cancellationToken);
}

static string CreateConnectHandshake(PeerId peerId, string messageId) =>
    ProtocolJson.Serialize(PartyGameKitMessages.Create(
        ProtocolMessageTypes.ConnectRequest,
        messageId,
        new ConnectRequestPayload(peerId)));

static string CreateResumeHandshake(PeerId peerId, string resumeToken, string messageId) =>
    ProtocolJson.Serialize(PartyGameKitMessages.Create(
        ProtocolMessageTypes.ResumeRequest,
        messageId,
        new ResumeRequestPayload(peerId, resumeToken)));

static byte[] CreateApplicationBytes(string applicationType, object data, string messageId)
{
    var payload = new ApplicationMessagePayload(
        applicationType,
        JsonSerializer.SerializeToElement(data));
    return Utf8(ProtocolJson.Serialize(PartyGameKitMessages.Create(
        ProtocolMessageTypes.ApplicationMessage,
        messageId,
        payload)));
}

static async Task<ProtocolEnvelope<TPayload>> ReadProtocolAsync<TPayload>(
    LanWebSocketClient client,
    string expectedType,
    CancellationToken cancellationToken)
{
    var frame = await client.ReceiveAsync(cancellationToken);
    Ensure(!frame.IsClose, $"Connection closed while waiting for {expectedType}: {frame.CloseDescription}.");
    var json = Encoding.UTF8.GetString(frame.Payload.Span);
    return RequireMessage<TPayload>(json, expectedType);
}

static ProtocolEnvelope<TPayload> RequireMessage<TPayload>(string json, string expectedType)
{
    var result = ProtocolJson.Read<TPayload>(json, expectedType);
    Ensure(result.IsSuccess && result.Message is not null, $"Invalid {expectedType} protocol message.");
    return result.Message!;
}

static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
{
    while (!condition())
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(20, cancellationToken);
    }
}

static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record ReceivedApplication(
    ConnectionId ConnectionId,
    string ApplicationType,
    JsonElement Data);
