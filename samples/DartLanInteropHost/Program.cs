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

await using var transport = await LanWebSocketTransport.StartAsync(
    new LanWebSocketHostOptions(IPAddress.Loopback),
    () => "dart-interop-connection",
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

var applicationReceived = false;

await foreach (var transportEvent in transport.ReadEventsAsync(cancellationToken))
{
    switch (transportEvent)
    {
        case TransportConnectionClosed closed when !applicationReceived:
            throw new InvalidOperationException(
                $"Dart client closed before completing interop exchange: {closed.Reason}.");

        case TransportMessageReceived received:
        {
            var json = Encoding.UTF8.GetString(received.Payload.Span);
            using var document = JsonDocument.Parse(json);
            var type = document.RootElement.GetProperty("type").GetString();

            if (type == ProtocolMessageTypes.ConnectRequest)
            {
                var request = RequireMessage<ConnectRequestPayload>(
                    json,
                    ProtocolMessageTypes.ConnectRequest);
                var peerId = request.Payload.PeerId;
                var accepted = DihorGameKitNetworkingMessages.Create(
                    ProtocolMessageTypes.ConnectAccepted,
                    "dotnet-dart-accepted",
                    new ConnectAcceptedPayload(
                        received.ConnectionId,
                        peerId,
                        peerId is null ? null : "dart-interop-resume-token"),
                    request.MessageId);

                await transport.SendAsync(
                    received.ConnectionId,
                    Utf8(ProtocolJson.Serialize(accepted)),
                    cancellationToken);
                break;
            }

            if (type == ProtocolMessageTypes.ApplicationMessage)
            {
                var message = RequireMessage<ApplicationMessagePayload>(
                    json,
                    ProtocolMessageTypes.ApplicationMessage);

                if (!string.Equals(
                        message.Payload.ApplicationType,
                        "interop.dart.ping",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unexpected Dart application type '{message.Payload.ApplicationType}'.");
                }

                if (!message.Payload.Data.TryGetProperty("value", out var value) ||
                    value.GetInt32() != 42)
                {
                    throw new InvalidOperationException(
                        "Dart opaque application payload was not preserved.");
                }

                var reply = DihorGameKitNetworkingMessages.Create(
                    ProtocolMessageTypes.ApplicationMessage,
                    "dotnet-dart-reply",
                    new ApplicationMessagePayload(
                        "interop.dotnet.pong",
                        JsonSerializer.SerializeToElement(new { value = 42, runtime = "dotnet" })),
                    message.MessageId);

                await transport.SendAsync(
                    received.ConnectionId,
                    Utf8(ProtocolJson.Serialize(reply)),
                    cancellationToken);
                applicationReceived = true;
                break;
            }

            break;
        }
    }

    if (applicationReceived)
    {
        break;
    }
}

await transport.StopAsync(cancellationToken);
Console.WriteLine("Dart -> .NET LAN protocol-v2 interop passed.");

static ProtocolEnvelope<TPayload> RequireMessage<TPayload>(
    string json,
    string expectedType)
{
    var result = ProtocolJson.Read<TPayload>(json, expectedType);
    if (!result.IsSuccess || result.Message is null)
    {
        throw new InvalidOperationException(
            $"Invalid '{expectedType}' protocol-v2 message.");
    }

    return result.Message;
}

static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
