using System.Text;
using System.Text.Json;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Runtime;
using Dihor.GameKit.Networking.Transport.InMemory;

namespace Dihor.GameKit.Networking.Runtime.Tests;

public sealed class ConnectionHostRuntimeTests
{
    [Fact]
    public async Task ConnectAndApplicationMessageAreHandledByRuntime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var transport = new InMemoryTransport();
        await using var host = new ConnectionHostRuntime(
            transport,
            messageIdFactory: SequenceIds("host"));
        await host.StartAsync(cancellationToken);
        await using var peer = await transport.OpenConnectionAsync(new ConnectionId("connection-1"), cancellationToken);
        await using var hostEvents = host.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var peerMessages = peer.ReadMessagesAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

        var peerId = new PeerId("peer-1");
        await peer.SendAsync(Serialize(
            ProtocolMessageTypes.ConnectRequest,
            "connect-1",
            new ConnectRequestPayload(peerId)), cancellationToken);

        Assert.True(await hostEvents.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));
        var connected = Assert.IsType<ConnectionPeerConnected>(hostEvents.Current);
        Assert.Equal(peerId, connected.PeerId);
        Assert.False(connected.IsResume);

        Assert.True(await peerMessages.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));
        var acceptanceJson = Encoding.UTF8.GetString(peerMessages.Current.Span);
        var acceptance = ProtocolJson.Read<ConnectAcceptedPayload>(
            acceptanceJson,
            ProtocolMessageTypes.ConnectAccepted);
        Assert.True(acceptance.IsSuccess);
        var resumeToken = acceptance.Message?.Payload.ResumeToken;
        Assert.False(string.IsNullOrWhiteSpace(resumeToken));

        using var dataDocument = JsonDocument.Parse("{\"choice\":2}");
        var application = DihorGameKitNetworkingMessages.Create(
            ProtocolMessageTypes.ApplicationMessage,
            "application-1",
            new ApplicationMessagePayload("game.choice", dataDocument.RootElement));
        var applicationBytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(application));
        await peer.SendAsync(applicationBytes, cancellationToken);
        await peer.SendAsync(applicationBytes, cancellationToken);

        Assert.True(await hostEvents.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));
        var received = Assert.IsType<ConnectionApplicationMessage>(hostEvents.Current);
        Assert.Equal("application-1", received.MessageId);
        Assert.Equal("game.choice", received.ApplicationType);
        Assert.Equal(2, received.Data.GetProperty("choice").GetInt32());

        await peer.DisposeAsync();
        Assert.True(await hostEvents.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));
        var disconnected = Assert.IsType<ConnectionPeerDisconnected>(hostEvents.Current);
        Assert.Equal(peerId, disconnected.PeerId);
        Assert.True(disconnected.CanResume);

        await using var replacement = await transport.OpenConnectionAsync(
            new ConnectionId("connection-2"),
            cancellationToken);
        await using var replacementMessages = replacement
            .ReadMessagesAsync(cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        await replacement.SendAsync(Serialize(
            ProtocolMessageTypes.ResumeRequest,
            "resume-1",
            new ResumeRequestPayload(peerId, resumeToken!)), cancellationToken);

        Assert.True(await hostEvents.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));
        var resumed = Assert.IsType<ConnectionPeerConnected>(hostEvents.Current);
        Assert.Equal(peerId, resumed.PeerId);
        Assert.True(resumed.IsResume);
        Assert.True(await replacementMessages.MoveNextAsync().AsTask().WaitAsync(
            TimeSpan.FromSeconds(2),
            cancellationToken));
        var resumeJson = Encoding.UTF8.GetString(replacementMessages.Current.Span);
        var resumeAcceptance = ProtocolJson.Read<ResumeAcceptedPayload>(
            resumeJson,
            ProtocolMessageTypes.ResumeAccepted);
        Assert.True(resumeAcceptance.IsSuccess);
        Assert.NotEqual(resumeToken, resumeAcceptance.Message?.Payload.ResumeToken);
    }

    private static ReadOnlyMemory<byte> Serialize<TPayload>(
        string type,
        string messageId,
        TPayload payload) =>
        Encoding.UTF8.GetBytes(ProtocolJson.Serialize(
            DihorGameKitNetworkingMessages.Create(type, messageId, payload)));

    private static Func<string> SequenceIds(string prefix)
    {
        var sequence = 0;
        return () => $"{prefix}-{Interlocked.Increment(ref sequence)}";
    }
}
