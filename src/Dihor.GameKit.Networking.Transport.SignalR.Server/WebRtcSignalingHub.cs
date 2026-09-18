using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Dihor.GameKit.Networking.Core;

namespace Dihor.GameKit.Networking.Transport.SignalR.Server;

public sealed class WebRtcSignalingServerOptions
{
    public int MaxSignalBytes { get; set; } = 64 * 1024;

    internal void Validate()
    {
        if (MaxSignalBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSignalBytes),
                MaxSignalBytes,
                "Signal limit must be positive.");
        }
    }
}

public static class WebRtcSignalingServiceCollectionExtensions
{
    public static IServiceCollection AddDihorGameKitNetworkingWebRtcSignaling(
        this IServiceCollection services,
        Action<WebRtcSignalingServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new WebRtcSignalingServerOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<WebRtcSignalingRegistry>();
        services
            .AddSignalR()
            .AddHubOptions<WebRtcSignalingHub>(hubOptions =>
            {
                hubOptions.MaximumReceiveMessageSize = checked(options.MaxSignalBytes + 16_384L);
            });
        return services;
    }
}

public static class WebRtcSignalingEndpointRouteBuilderExtensions
{
    public static HubEndpointConventionBuilder MapDihorGameKitNetworkingWebRtcSignaling(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/dihor-gamekit-networking-webrtc-signaling")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return endpoints.MapHub<WebRtcSignalingHub>(pattern);
    }
}

internal sealed class WebRtcSignalingHub(
    WebRtcSignalingRegistry registry,
    WebRtcSignalingServerOptions options) : Hub
{
    private const string PeerJoinedMethod = "Dihor.GameKit.Networking.WebRtcPeerJoined";
    private const string PeerLeftMethod = "Dihor.GameKit.Networking.WebRtcPeerLeft";
    private const string SignalMethod = "Dihor.GameKit.Networking.WebRtcSignal";

    public async Task<IReadOnlyList<string>> JoinChannel(string channelId)
    {
        var existingPeers = registry.Join(Context.ConnectionId, ParseChannelId(channelId));
        if (existingPeers.Count > 0)
        {
            await Clients.Clients(existingPeers)
                .SendAsync(PeerJoinedMethod, Context.ConnectionId, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        return existingPeers;
    }

    public async Task SendSignal(string targetConnectionId, string signalJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(signalJson);

        if (string.Equals(targetConnectionId, Context.ConnectionId, StringComparison.Ordinal))
        {
            throw new HubException("A signaling connection cannot target itself.");
        }

        var signalBytes = Encoding.UTF8.GetByteCount(signalJson);
        if (signalBytes > options.MaxSignalBytes)
        {
            throw new HubException(
                $"WebRTC signaling payload exceeds the configured limit of {options.MaxSignalBytes} bytes.");
        }

        registry.EnsureCanSignal(Context.ConnectionId, targetConnectionId);
        await Clients.Client(targetConnectionId)
            .SendAsync(
                SignalMethod,
                Context.ConnectionId,
                signalJson,
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task LeaveChannel()
    {
        var remainingPeers = registry.Leave(Context.ConnectionId);
        if (remainingPeers.Count > 0)
        {
            await Clients.Clients(remainingPeers)
                .SendAsync(PeerLeftMethod, Context.ConnectionId, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var remainingPeers = registry.Leave(Context.ConnectionId);
        if (remainingPeers.Count > 0)
        {
            await Clients.Clients(remainingPeers)
                .SendAsync(PeerLeftMethod, Context.ConnectionId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private static ChannelId ParseChannelId(string value)
    {
        try
        {
            return new ChannelId(value);
        }
        catch (ArgumentException exception)
        {
            throw new HubException("Invalid signaling channel identifier.", exception);
        }
    }
}

internal sealed class WebRtcSignalingRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<ChannelId, HashSet<string>> _channels = new();
    private readonly Dictionary<string, ChannelId> _channelByConnection = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Join(string signalRConnectionId, ChannelId channelId)
    {
        lock (_gate)
        {
            if (_channelByConnection.TryGetValue(signalRConnectionId, out var currentChannel))
            {
                if (currentChannel == channelId)
                {
                    return GetOtherPeersCore(signalRConnectionId, channelId);
                }

                throw new HubException("This SignalR connection is already registered for another signaling channel.");
            }

            if (!_channels.TryGetValue(channelId, out var peers))
            {
                peers = new HashSet<string>(StringComparer.Ordinal);
                _channels.Add(channelId, peers);
            }

            var existingPeers = peers.ToArray();
            peers.Add(signalRConnectionId);
            _channelByConnection.Add(signalRConnectionId, channelId);
            return existingPeers;
        }
    }

    public void EnsureCanSignal(string sourceConnectionId, string targetConnectionId)
    {
        lock (_gate)
        {
            if (!_channelByConnection.TryGetValue(sourceConnectionId, out var sourceChannel) ||
                !_channelByConnection.TryGetValue(targetConnectionId, out var targetChannel) ||
                sourceChannel != targetChannel)
            {
                throw new HubException("Signaling target is not connected to the same technical channel.");
            }
        }
    }

    public IReadOnlyList<string> Leave(string signalRConnectionId)
    {
        lock (_gate)
        {
            if (!_channelByConnection.Remove(signalRConnectionId, out var channelId) ||
                !_channels.TryGetValue(channelId, out var peers))
            {
                return Array.Empty<string>();
            }

            peers.Remove(signalRConnectionId);
            if (peers.Count == 0)
            {
                _channels.Remove(channelId);
                return Array.Empty<string>();
            }

            return peers.ToArray();
        }
    }

    private string[] GetOtherPeersCore(string signalRConnectionId, ChannelId channelId)
    {
        if (!_channels.TryGetValue(channelId, out var peers))
        {
            return Array.Empty<string>();
        }

        return peers.Where(peer => !string.Equals(peer, signalRConnectionId, StringComparison.Ordinal)).ToArray();
    }
}
