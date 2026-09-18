using System.Net;
using System.Text;
using System.Text.Json;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;
using Dihor.GameKit.Networking.Transport.Lan;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var cancellationToken = timeout.Token;

var nextConnection = 0;
await using var transport = await LanWebSocketTransport.StartAsync(
    new LanWebSocketHostOptions(IPAddress.Loopback),
    () => $"auto-demo-{Interlocked.Increment(ref nextConnection)}",
    cancellationToken);
var endpoint = transport.CreateClientUri(IPAddress.Loopback.ToString());
var hostLoop = RunHostAsync(transport, cancellationToken);

var selector = new AutomaticTransportSelector(
    [
        new TransportClientCandidate(
            TransportIds.LanWebSocket,
            async (handshake, token) => await LanWebSocketClient.ConnectAsync(
                endpoint,
                handshake,
                cancellationToken: token)),
    ]);

var peerId = new PeerId("auto-demo-peer");
var handshake = ProtocolJson.Serialize(GameKitNetworkingMessages.Create(
    ProtocolMessageTypes.ConnectRequest,
    "auto-connect-1",
    new ConnectRequestPayload(peerId)));

await using (var client = await selector.ConnectAsync(handshake, ConnectivityMode.Auto, cancellationToken))
{
    Ensure(client.TransportId == TransportIds.LanWebSocket, "Auto mode did not select the available LAN path.");

    var acceptedFrame = await client.ReceiveAsync(cancellationToken);
    Ensure(!acceptedFrame.IsClose, "Auto connection closed before the handshake was accepted.");
    var acceptedJson = Encoding.UTF8.GetString(acceptedFrame.Payload.Span);
    var accepted = ProtocolJson.Read<ConnectAcceptedPayload>(
        acceptedJson,
        ProtocolMessageTypes.ConnectAccepted);
    Ensure(
        accepted.IsSuccess && accepted.Message?.Payload.PeerId == peerId,
        "Auto sample did not preserve the stable peer identity.");

    var application = GameKitNetworkingMessages.Create(
        ProtocolMessageTypes.ApplicationMessage,
        "auto-message-1",
        new ApplicationMessagePayload(
            "sample.opaque",
            JsonSerializer.SerializeToElement(new { value = 42, text = "opaque" })));
    var payload = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(application));

    await client.SendAsync(payload, cancellationToken);
    var echoed = await client.ReceiveAsync(cancellationToken);

    Ensure(!echoed.IsClose, "Auto connection closed before the opaque payload was echoed.");
    Ensure(
        echoed.Payload.Span.SequenceEqual(payload),
        "Automatic connectivity changed the opaque application payload.");

    Console.WriteLine($"Auto selected: {client.TransportId}");
    foreach (var attempt in client.Diagnostics.Attempts)
    {
        Console.WriteLine($"  {attempt.TransportId}: {attempt.Outcome} ({attempt.Duration.TotalMilliseconds:F1} ms)");
    }
}

await transport.StopAsync(cancellationToken);
await hostLoop;
Console.WriteLine("Dihor.GameKit.Networking automatic connectivity demo passed.");

static async Task RunHostAsync(
    IMessageTransport transport,
    CancellationToken cancellationToken)
{
    await foreach (var transportEvent in transport.ReadEventsAsync(cancellationToken))
    {
        if (transportEvent is not TransportMessageReceived received)
        {
            continue;
        }

        var json = Encoding.UTF8.GetString(received.Payload.Span);
        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.GetProperty("type").GetString();

        if (type == ProtocolMessageTypes.ConnectRequest)
        {
            var request = ProtocolJson.Read<ConnectRequestPayload>(json, ProtocolMessageTypes.ConnectRequest);
            Ensure(request.IsSuccess && request.Message is not null, "Invalid connect request in Auto sample.");
            var peerId = request.Message!.Payload.PeerId
                ?? throw new InvalidOperationException("Auto sample requires a stable peer ID.");
            var accepted = GameKitNetworkingMessages.Create(
                ProtocolMessageTypes.ConnectAccepted,
                $"accepted-{received.ConnectionId.Value}",
                new ConnectAcceptedPayload(received.ConnectionId, peerId, "auto-demo-resume-token"),
                request.Message.MessageId);
            await transport.SendAsync(
                received.ConnectionId,
                Encoding.UTF8.GetBytes(ProtocolJson.Serialize(accepted)),
                cancellationToken);
            continue;
        }

        if (type == ProtocolMessageTypes.ApplicationMessage)
        {
            await transport.SendAsync(received.ConnectionId, received.Payload, cancellationToken);
        }
    }
}

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
