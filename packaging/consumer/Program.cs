using System.Text;
using System.Text.Json;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Discovery.Lan;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Runtime;
using Dihor.GameKit.Networking.Transport.Abstractions;
using Dihor.GameKit.Networking.Transport.InMemory;
using Dihor.GameKit.Networking.Transport.Lan;
using Dihor.GameKit.Networking.Transport.SignalR;
using Dihor.GameKit.Networking.Transport.SignalR.Server;

var channelId = new ChannelId("package-smoke-channel");
var descriptor = LanConnectionDescriptor.Create("127.0.0.1", 45678, channelId);

await using var transport = new InMemoryTransport();
var firstConnectionId = new ConnectionId("package-connection-1");
await using var firstPeer = await transport.OpenConnectionAsync(firstConnectionId);
await using var events = transport.ReadEventsAsync().GetAsyncEnumerator();

Ensure(await events.MoveNextAsync(), "Transport did not publish connection-open event.");
Ensure(events.Current is TransportConnectionOpened opened && opened.ConnectionId == firstConnectionId,
    "Unexpected connection-open event.");

var applicationBytes = CreateApplicationMessage("package.consumer.echo", new { text = "hello" }, "package-app-1");
await firstPeer.SendAsync(applicationBytes);
Ensure(await events.MoveNextAsync(), "Transport did not publish peer application message.");
Ensure(events.Current is TransportMessageReceived received && received.ConnectionId == firstConnectionId,
    "Unexpected inbound application event.");
ValidateApplicationMessage(((TransportMessageReceived)events.Current).Payload, "package.consumer.echo");

await using var outbound = firstPeer.ReadMessagesAsync().GetAsyncEnumerator();
await transport.SendAsync(firstConnectionId, CreateApplicationMessage(
    "package.consumer.reply",
    new { accepted = true },
    "package-app-2"));
Ensure(await outbound.MoveNextAsync(), "Peer did not receive targeted application message.");
ValidateApplicationMessage(outbound.Current, "package.consumer.reply");

var peerId = new PeerId("package-peer");
var continuity = new ConnectionContinuityCoordinator(
    new ConnectionContinuityOptions(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)),
    resumeTokenFactory: () => "package-resume-token");
var registration = continuity.Register(peerId, firstConnectionId);
Ensure(registration.IsConnected && registration.ResumeToken == "package-resume-token",
    "Stable peer registration failed.");

await firstPeer.DisposeAsync();
var disconnected = continuity.MarkDisconnected(firstConnectionId);
Ensure(disconnected.Status == DisconnectPeerStatus.Disconnected,
    "Peer was not moved into resumable state.");

var replacementConnectionId = new ConnectionId("package-connection-2");
await using var replacementPeer = await transport.OpenConnectionAsync(replacementConnectionId);
var resumed = continuity.Resume(peerId, registration.ResumeToken!, replacementConnectionId);
Ensure(resumed.IsResumed, "Peer resume failed.");
Ensure(continuity.PeerCount == 1, "Resume created a duplicate logical peer.");
Ensure(continuity.GetPresence(peerId)?.ConnectionId == replacementConnectionId,
    "Replacement connection was not bound to the stable peer.");

var timing = new MonotonicTimingSynchronizer();
var timingProbe = timing.CreateProbe(1_000, "package-timing-probe");
var timingObservation = timing.ObserveReply(
    new TimingProbeReply(timingProbe.ProbeId, 1_110, 1_112),
    1_022);
var normalizedTimestamp = timing.NormalizePeerTimestamp(1_150, 1_060);
Ensure(timingObservation.IsAccepted && timingObservation.Model?.OffsetMilliseconds == 100d,
    "Packaged monotonic timing offset estimation failed.");
Ensure(normalizedTimestamp.IsAccepted && normalizedTimestamp.ReferenceTimestampMilliseconds == 1_050d,
    "Packaged peer timestamp normalization failed.");

var relayOptions = new SignalRRelayOptions(
    new Uri("https://relay.example.test/dihor-gamekit-networking-relay"),
    channelId);
Ensure(relayOptions.ChannelId == channelId, "Packaged SignalR relay options are unavailable.");
var relayServerOptions = new SignalRRelayServerOptions();
Ensure(relayServerOptions.MaxMessageBytes > 0, "Packaged SignalR relay server options are unavailable.");

Console.WriteLine($"{descriptor.Transport}:{channelId.Value}:v{ProtocolVersions.Current}");
Console.WriteLine("Package-only opaque message exchange: OK");
Console.WriteLine("Package-only neutral peer resume: OK");
Console.WriteLine("Package-only monotonic timing normalization: OK");
Console.WriteLine(typeof(ConnectionHostRuntime).FullName);
Console.WriteLine(typeof(LanWebSocketTransport).FullName);
Console.WriteLine(typeof(UdpLanDiscoveryAdvertiser).FullName);
Console.WriteLine(typeof(SignalRRelayTransport).FullName);
Console.WriteLine(typeof(SignalRRelayServerOptions).FullName);

static byte[] CreateApplicationMessage(string applicationType, object data, string messageId)
{
    var payload = new ApplicationMessagePayload(
        applicationType,
        JsonSerializer.SerializeToElement(data));
    return Encoding.UTF8.GetBytes(ProtocolJson.Serialize(
        DihorGameKitNetworkingMessages.Create(
            ProtocolMessageTypes.ApplicationMessage,
            messageId,
            payload)));
}

static void ValidateApplicationMessage(ReadOnlyMemory<byte> payload, string expectedApplicationType)
{
    var json = Encoding.UTF8.GetString(payload.Span);
    var parsed = ProtocolJson.Read<ApplicationMessagePayload>(json, ProtocolMessageTypes.ApplicationMessage);
    Ensure(parsed.IsSuccess && parsed.Message?.Payload.ApplicationType == expectedApplicationType,
        $"Expected opaque application type '{expectedApplicationType}'.");
}

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
