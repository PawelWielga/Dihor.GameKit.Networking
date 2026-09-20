using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Runtime;
using Dihor.GameKit.Networking.Transport.Abstractions;

namespace Dihor.GameKit.Networking.Runtime.Tests;

public sealed class ConnectionClientRuntimeTests
{
    [Fact]
    public async Task ConnectStoresResumeCredentialAndSendsApplicationMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var peerId = new PeerId("peer-1");
        var client = new RecordingClient();
        client.Enqueue(Serialize(
            ProtocolMessageTypes.ConnectAccepted,
            "accepted-1",
            new ConnectAcceptedPayload(new ConnectionId("connection-1"), peerId, "resume-1")));
        var connector = new RecordingConnector(client);
        var credentials = new MemoryConnectionResumeCredentialStore();
        await using var runtime = new ConnectionClientRuntime(
            connector,
            credentials,
            new ConnectionClientOptions(TimeSpan.FromMinutes(1), TimeSpan.Zero),
            messageIdFactory: SequenceIds("client"));

        await runtime.ConnectAsync(peerId, "room:1", cancellationToken);

        Assert.Equal(ConnectionClientState.Connected, runtime.State);
        Assert.False(connector.LastWasReconnect);
        var connect = ProtocolJson.Read<ConnectRequestPayload>(
            connector.LastHandshake!,
            ProtocolMessageTypes.ConnectRequest);
        Assert.Equal(peerId, connect.Message?.Payload.PeerId);
        var stored = await credentials.ReadAsync("room:1", cancellationToken);
        Assert.Equal("resume-1", stored?.ResumeToken);

        using var dataDocument = JsonDocument.Parse("{\"ready\":true}");
        var messageId = await runtime.SendApplicationAsync(
            "lobby.ready",
            dataDocument.RootElement,
            cancellationToken);
        Assert.Equal("client-2", messageId);
        var sent = ProtocolJson.Read<ApplicationMessagePayload>(
            Encoding.UTF8.GetString(client.Sent.Single().Span),
            ProtocolMessageTypes.ApplicationMessage);
        Assert.Equal("lobby.ready", sent.Message?.Payload.ApplicationType);

        await runtime.DisconnectAsync(cancellationToken: cancellationToken);
        Assert.Equal(ConnectionClientState.Closed, runtime.State);
    }

    private static ClientTransportMessage Serialize<TPayload>(
        string type,
        string messageId,
        TPayload payload) =>
        new(Encoding.UTF8.GetBytes(ProtocolJson.Serialize(
            DihorGameKitNetworkingMessages.Create(type, messageId, payload))));

    private static Func<string> SequenceIds(string prefix)
    {
        var sequence = 0;
        return () => $"{prefix}-{Interlocked.Increment(ref sequence)}";
    }

    private sealed class RecordingConnector(IMessageTransportClient client) : IConnectionTransportConnector
    {
        public string? LastHandshake { get; private set; }

        public bool LastWasReconnect { get; private set; }

        public Task<IMessageTransportClient> ConnectAsync(
            string handshake,
            bool isReconnect,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastHandshake = handshake;
            LastWasReconnect = isReconnect;
            return Task.FromResult(client);
        }
    }

    private sealed class RecordingClient : IMessageTransportClient
    {
        private readonly Channel<ClientTransportMessage> _received =
            Channel.CreateUnbounded<ClientTransportMessage>();

        public List<ReadOnlyMemory<byte>> Sent { get; } = [];

        public void Enqueue(ClientTransportMessage message) => _received.Writer.TryWrite(message);

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add(new ReadOnlyMemory<byte>(payload.ToArray()));
            return ValueTask.CompletedTask;
        }

        public ValueTask<ClientTransportMessage> ReceiveAsync(
            CancellationToken cancellationToken = default) =>
            _received.Reader.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            _received.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
