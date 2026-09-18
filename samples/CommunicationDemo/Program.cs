using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Discovery.Lan;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;
using Dihor.GameKit.Networking.Transport.Lan;
using Dihor.GameKit.Networking.Transport.SignalR;
using Dihor.GameKit.Networking.Transport.SignalR.Server;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var cancellationToken = timeout.Token;
var mode = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "all";

switch (mode)
{
    case "all":
        await RunLanScenarioAsync(cancellationToken);
        await RunSignalRScenarioAsync(cancellationToken);
        break;
    case "lan":
        await RunLanScenarioAsync(cancellationToken);
        break;
    case "signalr":
        await RunSignalRScenarioAsync(cancellationToken);
        break;
    default:
        throw new ArgumentException("Communication demo mode must be 'all', 'lan' or 'signalr'.");
}

Console.WriteLine("Dihor.GameKit.Networking neutral communication demo passed.");

static async Task RunLanScenarioAsync(CancellationToken cancellationToken)
{
    var nextConnection = 0;
    await using var transport = await LanWebSocketTransport.StartAsync(
        new LanWebSocketHostOptions(IPAddress.Loopback),
        () => $"demo-connection-{Interlocked.Increment(ref nextConnection)}",
        cancellationToken);

    var descriptor = new ConnectionDescriptor(
        "lan-websocket",
        transport.CreateClientUri(IPAddress.Loopback.ToString()).AbsoluteUri,
        ProtocolVersions.Current,
        new ChannelId("communication-demo-lan"));
    var serializedDescriptor = ConnectionDescriptorCodec.SerializeUri(descriptor);
    var directDescriptor = ConnectionDescriptorCodec.ParseUri(serializedDescriptor);
    Ensure(directDescriptor == descriptor, "Serialized LAN connection descriptor did not round-trip.");

    await VerifyDiscoveryAsync(directDescriptor, cancellationToken);
    var endpoint = new Uri(directDescriptor.Endpoint);

    await RunScenarioAsync(
        "LAN",
        transport,
        async (handshake, token) =>
        {
            var client = await LanWebSocketClient.ConnectAsync(
                endpoint,
                handshake,
                cancellationToken: token);
            return new LanDemoClient(client);
        },
        cancellationToken);

    Console.WriteLine($"LAN descriptor: {serializedDescriptor}");
}

static async Task RunSignalRScenarioAsync(CancellationToken cancellationToken)
{
    await using var relayServer = await RelayDemoServer.StartAsync(cancellationToken);
    var channelId = new ChannelId("communication-demo-signalr");
    var options = new SignalRRelayOptions(relayServer.Endpoint, channelId);
    await using var transport = await SignalRRelayTransport.StartAsync(options, cancellationToken);

    var descriptor = new ConnectionDescriptor(
        "signalr-relay",
        relayServer.Endpoint.AbsoluteUri,
        ProtocolVersions.Current,
        channelId);
    var serializedDescriptor = ConnectionDescriptorCodec.SerializeUri(descriptor);
    var directDescriptor = ConnectionDescriptorCodec.ParseUri(serializedDescriptor);
    Ensure(directDescriptor == descriptor, "Serialized SignalR connection descriptor did not round-trip.");

    await RunScenarioAsync(
        "SignalR",
        transport,
        async (handshake, token) =>
        {
            var client = await SignalRRelayClient.ConnectAsync(options, handshake, token);
            return new SignalRDemoClient(client);
        },
        cancellationToken);

    Console.WriteLine($"SignalR descriptor: {serializedDescriptor}");
}

static async Task RunScenarioAsync(
    string transportName,
    IMessageTransport transport,
    Func<string, CancellationToken, Task<IDemoClient>> connectClient,
    CancellationToken cancellationToken)
{
    var continuity = new ConnectionContinuityCoordinator(
        new ConnectionContinuityOptions(
            peerTimeout: TimeSpan.FromSeconds(5),
            reconnectWindow: TimeSpan.FromSeconds(5)),
        resumeTokenFactory: () => $"resume-{Guid.NewGuid():N}");

    var receivedApplications = Channel.CreateUnbounded<ReceivedApplication>();
    var hostLoop = RunHostAsync(transport, continuity, receivedApplications.Writer, cancellationToken);

    var peerA = new PeerId("peer-a");
    var peerB = new PeerId("peer-b");

    await using var clientB = await connectClient(
        CreateConnectHandshake(peerB, $"{transportName}-connect-b"),
        cancellationToken);
    var acceptedB = await ReadProtocolAsync<ConnectAcceptedPayload>(
        clientB,
        ProtocolMessageTypes.ConnectAccepted,
        cancellationToken);
    Ensure(acceptedB.Payload.PeerId == peerB, $"{transportName}: peer B identity was not preserved.");

    var clientA = await connectClient(
        CreateConnectHandshake(peerA, $"{transportName}-connect-a"),
        cancellationToken);

    ProtocolEnvelope<ConnectAcceptedPayload> acceptedA;
    try
    {
        acceptedA = await ReadProtocolAsync<ConnectAcceptedPayload>(
            clientA,
            ProtocolMessageTypes.ConnectAccepted,
            cancellationToken);
        Ensure(acceptedA.Payload.PeerId == peerA, $"{transportName}: peer A identity was not preserved.");
        Ensure(
            !string.IsNullOrWhiteSpace(acceptedA.Payload.ResumeToken),
            $"{transportName}: peer A did not receive a resume token.");

        await clientA.SendAsync(
            CreateApplicationBytes("demo.from-peer", new { sender = "peer-a", value = 1 }, $"{transportName}-app-a-1"),
            cancellationToken);
        var fromPeer = await receivedApplications.Reader.ReadAsync(cancellationToken);
        Ensure(
            fromPeer.ConnectionId == acceptedA.Payload.ConnectionId,
            $"{transportName}: host observed the application message on the wrong connection.");
        Ensure(
            fromPeer.ApplicationType == "demo.from-peer",
            $"{transportName}: host did not preserve the consumer application type.");

        await transport.SendAsync(
            acceptedA.Payload.ConnectionId,
            CreateApplicationBytes("demo.targeted", new { recipient = "peer-a" }, $"{transportName}-targeted-1"),
            cancellationToken);
        var targeted = await ReadProtocolAsync<ApplicationMessagePayload>(
            clientA,
            ProtocolMessageTypes.ApplicationMessage,
            cancellationToken);
        Ensure(
            targeted.Payload.ApplicationType == "demo.targeted",
            $"{transportName}: targeted delivery failed.");

        await transport.BroadcastAsync(
            CreateApplicationBytes("demo.broadcast", new { audience = "all" }, $"{transportName}-broadcast-1"),
            cancellationToken);
        var broadcastA = await ReadProtocolAsync<ApplicationMessagePayload>(
            clientA,
            ProtocolMessageTypes.ApplicationMessage,
            cancellationToken);
        var broadcastB = await ReadProtocolAsync<ApplicationMessagePayload>(
            clientB,
            ProtocolMessageTypes.ApplicationMessage,
            cancellationToken);
        Ensure(
            broadcastA.Payload.ApplicationType == "demo.broadcast",
            $"{transportName}: peer A did not receive the broadcast.");
        Ensure(
            broadcastB.Payload.ApplicationType == "demo.broadcast",
            $"{transportName}: peer B did not receive the broadcast.");
    }
    finally
    {
        await clientA.DisposeAsync();
    }

    var originalConnectionA = acceptedA.Payload.ConnectionId;
    var resumeToken = acceptedA.Payload.ResumeToken!;
    await WaitUntilAsync(
        () => continuity.GetPresence(peerA)?.State == PeerConnectionState.AwaitingResume,
        cancellationToken);

    await using var resumedClientA = await connectClient(
        CreateResumeHandshake(peerA, resumeToken, $"{transportName}-resume-a"),
        cancellationToken);
    var resumedA = await ReadProtocolAsync<ResumeAcceptedPayload>(
        resumedClientA,
        ProtocolMessageTypes.ResumeAccepted,
        cancellationToken);

    Ensure(resumedA.Payload.PeerId == peerA, $"{transportName}: resume changed the stable peer identity.");
    Ensure(
        resumedA.Payload.ConnectionId != originalConnectionA,
        $"{transportName}: resume reused the old transient connection ID.");
    Ensure(continuity.PeerCount == 2, $"{transportName}: resume created a duplicate logical peer.");
    Ensure(
        continuity.GetPresence(peerA)?.ConnectionId == resumedA.Payload.ConnectionId,
        $"{transportName}: continuity coordinator did not bind the replacement connection.");

    await resumedClientA.SendAsync(
        CreateApplicationBytes("demo.after-resume", new { sender = "peer-a" }, $"{transportName}-app-a-2"),
        cancellationToken);
    var afterResume = await receivedApplications.Reader.ReadAsync(cancellationToken);
    Ensure(
        afterResume.ConnectionId == resumedA.Payload.ConnectionId,
        $"{transportName}: application traffic did not continue on the replacement connection.");

    Console.WriteLine(
        $"{transportName}: verified two generic peers, opaque application messages, targeted delivery, broadcast and resume.");

    await transport.StopAsync(cancellationToken);
    await hostLoop;
}

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
            var accepted = DihorGameKitNetworkingMessages.Create(
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
            var accepted = DihorGameKitNetworkingMessages.Create(
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
    ProtocolJson.Serialize(DihorGameKitNetworkingMessages.Create(
        ProtocolMessageTypes.ConnectRequest,
        messageId,
        new ConnectRequestPayload(peerId)));

static string CreateResumeHandshake(PeerId peerId, string resumeToken, string messageId) =>
    ProtocolJson.Serialize(DihorGameKitNetworkingMessages.Create(
        ProtocolMessageTypes.ResumeRequest,
        messageId,
        new ResumeRequestPayload(peerId, resumeToken)));

static byte[] CreateApplicationBytes(string applicationType, object data, string messageId)
{
    var payload = new ApplicationMessagePayload(
        applicationType,
        JsonSerializer.SerializeToElement(data));
    return Utf8(ProtocolJson.Serialize(DihorGameKitNetworkingMessages.Create(
        ProtocolMessageTypes.ApplicationMessage,
        messageId,
        payload)));
}

static async Task<ProtocolEnvelope<TPayload>> ReadProtocolAsync<TPayload>(
    IDemoClient client,
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

internal interface IDemoClient : IAsyncDisposable
{
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);

    ValueTask<DemoClientMessage> ReceiveAsync(CancellationToken cancellationToken = default);
}

internal sealed record DemoClientMessage(
    ReadOnlyMemory<byte> Payload,
    bool IsClose = false,
    string? CloseDescription = null);

internal sealed class LanDemoClient(LanWebSocketClient client) : IDemoClient
{
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) =>
        client.SendAsync(payload, cancellationToken);

    public async ValueTask<DemoClientMessage> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var message = await client.ReceiveAsync(cancellationToken);
        return new DemoClientMessage(message.Payload, message.IsClose, message.CloseDescription);
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}

internal sealed class SignalRDemoClient(SignalRRelayClient client) : IDemoClient
{
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) =>
        client.SendAsync(payload, cancellationToken);

    public async ValueTask<DemoClientMessage> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var message = await client.ReceiveAsync(cancellationToken);
        return new DemoClientMessage(message.Payload, message.IsClose, message.CloseDescription);
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}

internal sealed record ReceivedApplication(
    ConnectionId ConnectionId,
    string ApplicationType,
    JsonElement Data);

internal sealed class RelayDemoServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    private RelayDemoServer(WebApplication application, Uri endpoint)
    {
        _application = application;
        Endpoint = endpoint;
    }

    public Uri Endpoint { get; }

    public static async Task<RelayDemoServer> StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RelayDemoServer).Assembly.FullName,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
        });
        builder.Services.AddDihorGameKitNetworkingSignalRRelay();

        var application = builder.Build();
        application.MapDihorGameKitNetworkingSignalRRelay();
        await application.StartAsync(cancellationToken);

        var server = application.Services.GetRequiredService<IServer>();
        var boundAddress = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        if (boundAddress is null || !Uri.TryCreate(boundAddress, UriKind.Absolute, out var baseUri))
        {
            await application.DisposeAsync();
            throw new InvalidOperationException("SignalR demo relay did not expose a bound address.");
        }

        return new RelayDemoServer(application, new Uri(baseUri, "/partygamekit-relay"));
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
