using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dihor.GameKit.Networking.Transport.SignalR.Server;

namespace Dihor.GameKit.Networking.Transport.Tests;

public sealed class WebRtcSignalingIntegrationTests
{
    private const string PeerJoinedMethod = "Dihor.GameKit.Networking.WebRtcPeerJoined";
    private const string PeerLeftMethod = "Dihor.GameKit.Networking.WebRtcPeerLeft";
    private const string SignalMethod = "Dihor.GameKit.Networking.WebRtcSignal";

    [Fact]
    public async Task SignalingRoutesOnlyWithinTechnicalChannel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await SignalingTestServer.StartAsync(cancellationToken);
        await using var peerA = CreateConnection(server.Endpoint);
        await using var peerB = CreateConnection(server.Endpoint);
        await using var peerC = CreateConnection(server.Endpoint);

        var peerJoined = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerLeft = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedSignal = new TaskCompletionSource<(string Source, string Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        peerA.On<string>(PeerJoinedMethod, id => peerJoined.TrySetResult(id));
        peerA.On<string>(PeerLeftMethod, id => peerLeft.TrySetResult(id));
        peerB.On<string, string>(
            SignalMethod,
            (source, payload) => receivedSignal.TrySetResult((source, payload)));

        await peerA.StartAsync(cancellationToken);
        await peerB.StartAsync(cancellationToken);
        await peerC.StartAsync(cancellationToken);

        var initialA = await peerA.InvokeAsync<string[]>(
            "JoinChannel",
            "channel-a",
            cancellationToken);
        Assert.Empty(initialA);

        var existingForB = await peerB.InvokeAsync<string[]>(
            "JoinChannel",
            "channel-a",
            cancellationToken);
        Assert.Equal([peerA.ConnectionId], existingForB);
        Assert.Equal(peerB.ConnectionId, await peerJoined.Task.WaitAsync(cancellationToken));

        var existingForC = await peerC.InvokeAsync<string[]>(
            "JoinChannel",
            "channel-b",
            cancellationToken);
        Assert.Empty(existingForC);

        const string offer = "{\"kind\":\"description\",\"description\":{\"type\":\"offer\",\"sdp\":\"opaque-sdp\"}}";
        await peerA.InvokeAsync(
            "SendSignal",
            peerB.ConnectionId,
            offer,
            cancellationToken);
        var signal = await receivedSignal.Task.WaitAsync(cancellationToken);
        Assert.Equal(peerA.ConnectionId, signal.Source);
        Assert.Equal(offer, signal.Payload);

        var crossChannel = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await peerA.InvokeAsync(
                "SendSignal",
                peerC.ConnectionId,
                offer,
                cancellationToken);
        });
        Assert.Contains("same technical channel", crossChannel.Message, StringComparison.OrdinalIgnoreCase);

        await peerB.InvokeAsync("LeaveChannel", cancellationToken);
        Assert.Equal(peerB.ConnectionId, await peerLeft.Task.WaitAsync(cancellationToken));
    }

    [Fact]
    public async Task SignalingPayloadLimitIsEnforcedByDihor.GameKit.NetworkingHubOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await SignalingTestServer.StartAsync(
            cancellationToken,
            maxSignalBytes: 128);
        await using var peerA = CreateConnection(server.Endpoint);
        await using var peerB = CreateConnection(server.Endpoint);

        await peerA.StartAsync(cancellationToken);
        await peerB.StartAsync(cancellationToken);
        await peerA.InvokeAsync<string[]>("JoinChannel", "limits", cancellationToken);
        await peerB.InvokeAsync<string[]>("JoinChannel", "limits", cancellationToken);

        var tooLarge = new string('x', 129);
        var exception = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await peerA.InvokeAsync(
                "SendSignal",
                peerB.ConnectionId,
                tooLarge,
                cancellationToken);
        });
        Assert.Contains("configured limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HubConnection CreateConnection(Uri endpoint) =>
        new HubConnectionBuilder()
            .WithUrl(endpoint)
            .Build();

    private sealed class SignalingTestServer : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private SignalingTestServer(WebApplication application, Uri endpoint)
        {
            _application = application;
            Endpoint = endpoint;
        }

        public Uri Endpoint { get; }

        public static async Task<SignalingTestServer> StartAsync(
            CancellationToken cancellationToken,
            int maxSignalBytes = 64 * 1024)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(SignalingTestServer).Assembly.FullName,
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddDihor.GameKit.NetworkingWebRtcSignaling(options =>
            {
                options.MaxSignalBytes = maxSignalBytes;
            });

            var application = builder.Build();
            application.MapDihor.GameKit.NetworkingWebRtcSignaling();
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
                throw new InvalidOperationException("Test signaling server did not expose a bound address.");
            }

            return new SignalingTestServer(
                application,
                new Uri(baseUri, "/partygamekit-webrtc-signaling"));
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
