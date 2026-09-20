using System.Net;
using System.Text;
using System.Text.Json;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;
using Dihor.GameKit.Networking.Transport.Lan;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    throw new ArgumentException("Expected one descriptor output file path.");
}

var descriptorFile = Path.GetFullPath(args[0]);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var cancellationToken = timeout.Token;

var nextConnection = 0;
await using var transport = await LanWebSocketTransport.StartAsync(
    new LanWebSocketHostOptions(IPAddress.Loopback),
    () => $"dart-interop-connection-{Interlocked.Increment(ref nextConnection)}",
    cancellationToken);

var descriptor = new ConnectionDescriptor(
    LanConnectionDescriptor.TransportName,
    transport.CreateClientUri(IPAddress.Loopback.ToString()).AbsoluteUri,
    ProtocolVersions.Current,
    new ChannelId("dart-lan-interop"));

var descriptorDirectory = Path.GetDirectoryName(descriptorFile);
if (!string.IsNullOrWhiteSpace(descriptorDirectory))
{
    Directory.CreateDirectory(descriptorDirectory);
}

await File.WriteAllTextAsync(
    descriptorFile,
    ConnectionDescriptorCodec.SerializeUri(descriptor),
    cancellationToken);

var continuity = new ConnectionContinuityCoordinator(
    new ConnectionContinuityOptions(
        peerTimeout: TimeSpan.FromSeconds(2),
        reconnectWindow: TimeSpan.FromSeconds(10)),
    resumeTokenFactory: () => $"resume-{Guid.NewGuid():N}");

ConnectionId? initialConnectionId = null;
var initialApplicationReceived = false;
var heartbeatReceived = false;
var initialDropIssued = false;
var resumed = false;
var afterResumeApplicationReceived = false;
var explicitDisconnectReceived = false;

await foreach (var transportEvent in transport.ReadEventsAsync(cancellationToken))
{
    switch (transportEvent)
    {
        case TransportConnectionClosed closed:
            continuity.MarkDisconnected(closed.ConnectionId);
            break;

        case TransportMessageReceived received:
        {
            var json = Encoding.UTF8.GetString(received.Payload.Span);
            using var document = JsonDocument.Parse(json);
            var type = document.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case ProtocolMessageTypes.ConnectRequest:
                {
                    var request = RequireMessage<ConnectRequestPayload>(
                        json,
                        ProtocolMessageTypes.ConnectRequest);
                    var peerId = request.Payload.PeerId
                        ?? throw new InvalidOperationException(
                            "Dart continuity interop requires a stable PeerId.");

                    var registration = continuity.Register(peerId, received.ConnectionId);
                    Ensure(
                        registration.IsConnected,
                        $"Unable to register Dart peer: {registration.Status}.");

                    initialConnectionId = received.ConnectionId;
                    var accepted = DihorGameKitNetworkingMessages.Create(
                        ProtocolMessageTypes.ConnectAccepted,
                        "dotnet-dart-accepted",
                        new ConnectAcceptedPayload(
                            received.ConnectionId,
                            peerId,
                            registration.ResumeToken),
                        request.MessageId);

                    await transport.SendAsync(
                        received.ConnectionId,
                        Utf8(ProtocolJson.Serialize(accepted)),
                        cancellationToken);
                    break;
                }

                case ProtocolMessageTypes.ResumeRequest:
                {
                    var request = RequireMessage<ResumeRequestPayload>(
                        json,
                        ProtocolMessageTypes.ResumeRequest);

                    var resume = continuity.Resume(
                        request.Payload.PeerId,
                        request.Payload.ResumeToken,
                        received.ConnectionId);
                    Ensure(
                        resume.IsResumed,
                        $"Unable to resume Dart peer: {resume.Status}.");
                    Ensure(
                        initialConnectionId is not null &&
                        received.ConnectionId != initialConnectionId.Value,
                        "Resume reused the original transient ConnectionId.");

                    var accepted = DihorGameKitNetworkingMessages.Create(
                        ProtocolMessageTypes.ResumeAccepted,
                        "dotnet-dart-resumed",
                        new ResumeAcceptedPayload(
                            received.ConnectionId,
                            request.Payload.PeerId,
                            resume.ResumeToken),
                        request.MessageId);

                    await transport.SendAsync(
                        received.ConnectionId,
                        Utf8(ProtocolJson.Serialize(accepted)),
                        cancellationToken);
                    resumed = true;
                    break;
                }

                case ProtocolMessageTypes.Heartbeat:
                {
                    var heartbeat = RequireMessage<HeartbeatPayload>(
                        json,
                        ProtocolMessageTypes.Heartbeat);
                    Ensure(
                        heartbeat.Payload.PeerId?.Value == "dart-interop-peer",
                        "Dart heartbeat did not preserve the neutral PeerId.");
                    Ensure(
                        document.RootElement.GetProperty("payload")
                            .EnumerateObject()
                            .All(property => property.Name == "peerId"),
                        "Heartbeat carried data outside the liveness control payload.");
                    Ensure(
                        continuity.RecordHeartbeat(received.ConnectionId),
                        "Dart heartbeat was not accepted for the active connection.");
                    heartbeatReceived = true;
                    break;
                }

                case ProtocolMessageTypes.ApplicationMessage:
                {
                    var message = RequireMessage<ApplicationMessagePayload>(
                        json,
                        ProtocolMessageTypes.ApplicationMessage);

                    if (!resumed)
                    {
                        Ensure(
                            message.Payload.ApplicationType == "interop.dart.before-resume",
                            $"Unexpected initial Dart application type '{message.Payload.ApplicationType}'.");
                        EnsureValue42(message.Payload.Data);

                        var reply = CreateApplicationReply(
                            "interop.dotnet.before-resume",
                            "dotnet-dart-before-reply",
                            message.MessageId);
                        await transport.SendAsync(
                            received.ConnectionId,
                            Utf8(ProtocolJson.Serialize(reply)),
                            cancellationToken);
                        initialApplicationReceived = true;
                    }
                    else
                    {
                        Ensure(
                            message.Payload.ApplicationType == "interop.dart.after-resume",
                            $"Unexpected resumed Dart application type '{message.Payload.ApplicationType}'.");
                        EnsureValue42(message.Payload.Data);

                        var reply = CreateApplicationReply(
                            "interop.dotnet.after-resume",
                            "dotnet-dart-after-reply",
                            message.MessageId);
                        await transport.SendAsync(
                            received.ConnectionId,
                            Utf8(ProtocolJson.Serialize(reply)),
                            cancellationToken);
                        afterResumeApplicationReceived = true;
                    }

                    break;
                }

                case ProtocolMessageTypes.Disconnect:
                {
                    var disconnect = RequireMessage<DisconnectPayload>(
                        json,
                        ProtocolMessageTypes.Disconnect);
                    Ensure(
                        disconnect.Payload.Reason == "interop-complete",
                        "Dart explicit disconnect reason was not preserved.");
                    explicitDisconnectReceived = true;
                    break;
                }
            }

            if (!initialDropIssued &&
                initialApplicationReceived &&
                heartbeatReceived &&
                initialConnectionId is { } connectionToDrop)
            {
                initialDropIssued = true;
                await transport.DisconnectAsync(
                    connectionToDrop,
                    TransportCloseReason.Faulted,
                    cancellationToken);
            }

            break;
        }
    }

    if (afterResumeApplicationReceived && explicitDisconnectReceived)
    {
        break;
    }
}

Ensure(resumed, "Dart client never completed protocol-v2 resume.");
Ensure(
    afterResumeApplicationReceived,
    "Dart client did not send application traffic after resume.");
Ensure(
    explicitDisconnectReceived,
    "Dart client did not send an explicit disconnect control message.");

await transport.StopAsync(cancellationToken);
Console.WriteLine(
    "Dart -> .NET LAN connect/heartbeat/resume/application/disconnect interop passed.");

static ProtocolEnvelope<ApplicationMessagePayload> CreateApplicationReply(
    string applicationType,
    string messageId,
    string correlationId) =>
    DihorGameKitNetworkingMessages.Create(
        ProtocolMessageTypes.ApplicationMessage,
        messageId,
        new ApplicationMessagePayload(
            applicationType,
            JsonSerializer.SerializeToElement(
                new { value = 42, runtime = "dotnet" })),
        correlationId);

static void EnsureValue42(JsonElement data)
{
    Ensure(
        data.TryGetProperty("value", out var value) &&
        value.GetInt32() == 42,
        "Dart opaque application payload was not preserved.");
}

static ProtocolEnvelope<TPayload> RequireMessage<TPayload>(
    string json,
    string expectedType)
{
    var result = ProtocolJson.Read<TPayload>(json, expectedType);
    Ensure(
        result.IsSuccess && result.Message is not null,
        $"Invalid '{expectedType}' protocol-v2 message.");
    return result.Message!;
}

static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
