using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.SignalR;
using PartyGameKit.Transport.SignalR.Server;

namespace PartyGameKit.Transport.Tests;

public sealed class SignalRRelayLimitIntegrationTests
{
    [Fact]
    public void RelayRegistrationDoesNotOverrideGlobalSignalROptions()
    {
        var services = new ServiceCollection();
        services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 12_345;
        });
        services.AddPartyGameKitSignalRRelay(options =>
        {
            options.MaxMessageBytes = 1024;
        });

        using var provider = services.BuildServiceProvider();
        var globalOptions = provider.GetRequiredService<IOptions<HubOptions>>().Value;

        Assert.Equal(12_345, globalOptions.MaximumReceiveMessageSize);
    }

    [Fact]
    public async Task LocalClientPayloadLimitClosesAndRemovesRelayBinding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await RelayTestServer.StartAsync(cancellationToken);
        var channel = new ChannelId("client-limit");
        await using var transport = await SignalRRelayTransport.StartAsync(
            new SignalRRelayOptions(server.Endpoint, channel),
            cancellationToken);
        await using var events = transport
            .ReadEventsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var clientOptions = new SignalRRelayOptions(
            server.Endpoint,
            channel,
            maxMessageBytes: 256);
        await using var client = await SignalRRelayClient.ConnectAsync(
            clientOptions,
            CreateConnectHandshake(),
            cancellationToken);

        var opened = await ReadEventAsync<TransportConnectionOpened>(events);
        Assert.Equal(client.ConnectionId, opened.ConnectionId);
        var handshake = await ReadEventAsync<TransportMessageReceived>(events);
        Assert.Equal(client.ConnectionId, handshake.ConnectionId);

        await transport.SendAsync(
            client.ConnectionId,
            new byte[512],
            cancellationToken);

        var closedClient = await client.ReceiveAsync(cancellationToken);
        Assert.True(closedClient.IsClose);
        Assert.Equal("message-too-large", closedClient.CloseDescription);

        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
        {
            await client.ReceiveAsync(TestContext.Current.CancellationToken);
        });

        var closedTransport = await ReadEventAsync<TransportConnectionClosed>(events);
        Assert.Equal(client.ConnectionId, closedTransport.ConnectionId);
        Assert.Equal(TransportCloseReason.RemoteClosed, closedTransport.Reason);
        Assert.Equal(0, transport.ConnectionCount);
    }

    private static string CreateConnectHandshake() =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.ConnectRequest,
            "limit-connect",
            new ConnectRequestPayload(new PeerId("limit-peer"))));

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

        throw new InvalidOperationException(
            $"Transport event stream ended before {typeof(TEvent).Name} was observed.");
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
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddPartyGameKitSignalRRelay();

            var application = builder.Build();
            application.MapPartyGameKitSignalRRelay();
            await application.StartAsync(cancellationToken);

            var server = application.Services.GetRequiredService<IServer>();
            var boundAddress = server.Features
                .Get<IServerAddressesFeature>()?
                .Addresses
                .FirstOrDefault();
            if (boundAddress is null ||
                !Uri.TryCreate(boundAddress, UriKind.Absolute, out var baseUri))
            {
                await application.DisposeAsync();
                throw new InvalidOperationException("Test relay server did not expose a bound address.");
            }

            return new RelayTestServer(
                application,
                new Uri(baseUri, "/partygamekit-relay"));
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
