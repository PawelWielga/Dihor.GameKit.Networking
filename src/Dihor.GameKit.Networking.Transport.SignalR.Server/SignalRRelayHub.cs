using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Dihor.GameKit.Networking.Core;

namespace Dihor.GameKit.Networking.Transport.SignalR.Server;

public sealed class SignalRRelayServerOptions
{
    public int MaxMessageBytes { get; set; } = 256 * 1024;

    internal void Validate()
    {
        if (MaxMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxMessageBytes),
                MaxMessageBytes,
                "Message limit must be positive.");
        }
    }
}

public static class SignalRRelayServiceCollectionExtensions
{
    public static IServiceCollection AddGameKitNetworkingSignalRRelay(
        this IServiceCollection services,
        Action<SignalRRelayServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SignalRRelayServerOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<SignalRRelayRegistry>();
        services.AddSignalR()
            .AddHubOptions<SignalRRelayHub>(hubOptions =>
            {
                // The default JSON SignalR protocol represents byte[] as base64. Keep the
                // Dihor.GameKit.Networking payload limit authoritative while allowing enough framing
                // overhead for a payload at that limit to reach this hub for validation.
                var base64Bytes = (((long)options.MaxMessageBytes + 2L) / 3L) * 4L;
                hubOptions.MaximumReceiveMessageSize = checked(base64Bytes + 16_384L);
            });
        return services;
    }
}

internal sealed class SignalRRelayHub(
    SignalRRelayRegistry registry,
    SignalRRelayServerOptions options) : Hub
{
    private const string PeerConnectedMethod = "PartyGameKit.PeerConnected";
    private const string PeerMessageMethod = "PartyGameKit.PeerMessage";
    private const string PeerDisconnectedMethod = "PartyGameKit.PeerDisconnected";
    private const string ClientMessageMethod = "PartyGameKit.Message";
    private const string ClientDisconnectedMethod = "PartyGameKit.Disconnected";

    public Task RegisterListener(string channelId)
    {
        registry.RegisterListener(Context.ConnectionId, ParseChannelId(channelId));
        return Task.CompletedTask;
    }

    public async Task<string> AttachClient(string channelId, byte[] handshake)
    {
        ValidatePayload(handshake);
        var binding = registry.AttachClient(Context.ConnectionId, ParseChannelId(channelId));
        try
        {
            await Clients.Client(binding.ListenerSignalRConnectionId)
                .SendAsync(
                    PeerConnectedMethod,
                    binding.ConnectionId.Value,
                    handshake,
                    Context.ConnectionAborted)
                .ConfigureAwait(false);
            return binding.ConnectionId.Value;
        }
        catch
        {
            registry.RemoveClientBySignalRConnection(Context.ConnectionId);
            throw;
        }
    }

    public async Task SendFromClient(string connectionId, byte[] payload)
    {
        ValidatePayload(payload);
        var binding = registry.GetClientForCaller(
            Context.ConnectionId,
            ParseConnectionId(connectionId));
        await Clients.Client(binding.ListenerSignalRConnectionId)
            .SendAsync(
                PeerMessageMethod,
                binding.ConnectionId.Value,
                payload,
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task SendToClient(string connectionId, byte[] payload)
    {
        ValidatePayload(payload);
        var binding = registry.GetClientForListener(
            Context.ConnectionId,
            ParseConnectionId(connectionId));
        await Clients.Client(binding.SignalRConnectionId)
            .SendAsync(ClientMessageMethod, payload, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task Broadcast(byte[] payload)
    {
        ValidatePayload(payload);
        var clients = registry.GetClientsForListener(Context.ConnectionId);
        if (clients.Count == 0)
        {
            return;
        }

        await Clients.Clients(clients.Select(client => client.SignalRConnectionId))
            .SendAsync(ClientMessageMethod, payload, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task DisconnectClient(string connectionId, string? reason = null)
    {
        var binding = registry.RemoveClientForListener(
            Context.ConnectionId,
            ParseConnectionId(connectionId));
        var normalizedReason = NormalizeReason(reason, "normal");

        await Clients.Client(binding.SignalRConnectionId)
            .SendAsync(ClientDisconnectedMethod, normalizedReason, Context.ConnectionAborted)
            .ConfigureAwait(false);
        await Clients.Caller
            .SendAsync(
                PeerDisconnectedMethod,
                binding.ConnectionId.Value,
                normalizedReason,
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task DetachClient(string connectionId)
    {
        var binding = registry.RemoveClientForCaller(
            Context.ConnectionId,
            ParseConnectionId(connectionId));
        await Clients.Client(binding.ListenerSignalRConnectionId)
            .SendAsync(
                PeerDisconnectedMethod,
                binding.ConnectionId.Value,
                "remote-closed",
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public async Task UnregisterListener()
    {
        var clients = registry.RemoveListener(Context.ConnectionId);
        if (clients.Count == 0)
        {
            return;
        }

        await Clients.Clients(clients.Select(client => client.SignalRConnectionId))
            .SendAsync(ClientDisconnectedMethod, "transport-stopped", Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var listenerClients = registry.RemoveListenerBySignalRConnection(Context.ConnectionId);
        if (listenerClients is not null)
        {
            if (listenerClients.Count > 0)
            {
                await Clients.Clients(listenerClients.Select(client => client.SignalRConnectionId))
                    .SendAsync(ClientDisconnectedMethod, "transport-stopped", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            var client = registry.RemoveClientBySignalRConnection(Context.ConnectionId);
            if (client is not null)
            {
                await Clients.Client(client.ListenerSignalRConnectionId)
                    .SendAsync(
                        PeerDisconnectedMethod,
                        client.ConnectionId.Value,
                        "remote-closed",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private void ValidatePayload(byte[]? payload)
    {
        if (payload is null)
        {
            throw new HubException("Payload is required.");
        }

        if (payload.Length > options.MaxMessageBytes)
        {
            throw new HubException(
                $"Payload exceeds the configured relay limit of {options.MaxMessageBytes} bytes.");
        }
    }

    private static ChannelId ParseChannelId(string value)
    {
        try
        {
            return new ChannelId(value);
        }
        catch (ArgumentException exception)
        {
            throw new HubException("Invalid channel identifier.", exception);
        }
    }

    private static ConnectionId ParseConnectionId(string value)
    {
        try
        {
            return new ConnectionId(value);
        }
        catch (ArgumentException exception)
        {
            throw new HubException("Invalid connection identifier.", exception);
        }
    }

    private static string NormalizeReason(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }
}

internal sealed class SignalRRelayRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<ChannelId, RelayChannel> _channels = new();
    private readonly Dictionary<string, ChannelId> _listenerChannels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelayClientBinding> _clientsBySignalRConnection = new(StringComparer.Ordinal);

    public void RegisterListener(string signalRConnectionId, ChannelId channelId)
    {
        lock (_gate)
        {
            if (_clientsBySignalRConnection.ContainsKey(signalRConnectionId))
            {
                throw new HubException("A relay client connection cannot also register as a listener.");
            }

            if (_listenerChannels.TryGetValue(signalRConnectionId, out var currentChannel))
            {
                if (currentChannel == channelId)
                {
                    return;
                }

                throw new HubException("This SignalR connection is already registered for another channel.");
            }

            if (_channels.ContainsKey(channelId))
            {
                throw new HubException($"Channel '{channelId}' already has a listener.");
            }

            _channels.Add(channelId, new RelayChannel(signalRConnectionId));
            _listenerChannels.Add(signalRConnectionId, channelId);
        }
    }

    public RelayClientBinding AttachClient(string signalRConnectionId, ChannelId channelId)
    {
        lock (_gate)
        {
            if (_listenerChannels.ContainsKey(signalRConnectionId) ||
                _clientsBySignalRConnection.ContainsKey(signalRConnectionId))
            {
                throw new HubException("This SignalR connection is already attached to the relay.");
            }

            if (!_channels.TryGetValue(channelId, out var channel))
            {
                throw new HubException($"Channel '{channelId}' has no active listener.");
            }

            for (var attempt = 0; attempt < 100; attempt++)
            {
                var connectionId = new ConnectionId($"signalr-{Guid.NewGuid():N}");
                if (channel.Clients.ContainsKey(connectionId))
                {
                    continue;
                }

                var binding = new RelayClientBinding(
                    signalRConnectionId,
                    channel.ListenerSignalRConnectionId,
                    channelId,
                    connectionId);
                channel.Clients.Add(connectionId, binding);
                _clientsBySignalRConnection.Add(signalRConnectionId, binding);
                return binding;
            }

            throw new HubException("Unable to allocate a unique relay connection identifier.");
        }
    }

    public RelayClientBinding GetClientForCaller(
        string signalRConnectionId,
        ConnectionId connectionId)
    {
        lock (_gate)
        {
            if (!_clientsBySignalRConnection.TryGetValue(signalRConnectionId, out var binding) ||
                binding.ConnectionId != connectionId)
            {
                throw new HubException("Relay client binding was not found.");
            }

            return binding;
        }
    }

    public RelayClientBinding GetClientForListener(
        string listenerSignalRConnectionId,
        ConnectionId connectionId)
    {
        lock (_gate)
        {
            return GetClientForListenerCore(listenerSignalRConnectionId, connectionId);
        }
    }

    public IReadOnlyList<RelayClientBinding> GetClientsForListener(string listenerSignalRConnectionId)
    {
        lock (_gate)
        {
            if (!_listenerChannels.TryGetValue(listenerSignalRConnectionId, out var channelId) ||
                !_channels.TryGetValue(channelId, out var channel))
            {
                throw new HubException("Relay listener registration was not found.");
            }

            return channel.Clients.Values.ToArray();
        }
    }

    public RelayClientBinding RemoveClientForListener(
        string listenerSignalRConnectionId,
        ConnectionId connectionId)
    {
        lock (_gate)
        {
            var binding = GetClientForListenerCore(listenerSignalRConnectionId, connectionId);
            RemoveClientCore(binding);
            return binding;
        }
    }

    public RelayClientBinding RemoveClientForCaller(
        string signalRConnectionId,
        ConnectionId connectionId)
    {
        lock (_gate)
        {
            if (!_clientsBySignalRConnection.TryGetValue(signalRConnectionId, out var binding) ||
                binding.ConnectionId != connectionId)
            {
                throw new HubException("Relay client binding was not found.");
            }

            RemoveClientCore(binding);
            return binding;
        }
    }

    public RelayClientBinding? RemoveClientBySignalRConnection(string signalRConnectionId)
    {
        lock (_gate)
        {
            if (!_clientsBySignalRConnection.TryGetValue(signalRConnectionId, out var binding))
            {
                return null;
            }

            RemoveClientCore(binding);
            return binding;
        }
    }

    public IReadOnlyList<RelayClientBinding> RemoveListener(string listenerSignalRConnectionId)
    {
        lock (_gate)
        {
            if (!_listenerChannels.TryGetValue(listenerSignalRConnectionId, out var channelId))
            {
                throw new HubException("Relay listener registration was not found.");
            }

            return RemoveListenerCore(listenerSignalRConnectionId, channelId);
        }
    }

    public IReadOnlyList<RelayClientBinding>? RemoveListenerBySignalRConnection(
        string listenerSignalRConnectionId)
    {
        lock (_gate)
        {
            if (!_listenerChannels.TryGetValue(listenerSignalRConnectionId, out var channelId))
            {
                return null;
            }

            return RemoveListenerCore(listenerSignalRConnectionId, channelId);
        }
    }

    private RelayClientBinding GetClientForListenerCore(
        string listenerSignalRConnectionId,
        ConnectionId connectionId)
    {
        if (!_listenerChannels.TryGetValue(listenerSignalRConnectionId, out var channelId) ||
            !_channels.TryGetValue(channelId, out var channel) ||
            !channel.Clients.TryGetValue(connectionId, out var binding))
        {
            throw new HubException("Relay client binding was not found.");
        }

        return binding;
    }

    private void RemoveClientCore(RelayClientBinding binding)
    {
        _clientsBySignalRConnection.Remove(binding.SignalRConnectionId);
        if (_channels.TryGetValue(binding.ChannelId, out var channel))
        {
            channel.Clients.Remove(binding.ConnectionId);
        }
    }

    private RelayClientBinding[] RemoveListenerCore(
        string listenerSignalRConnectionId,
        ChannelId channelId)
    {
        _listenerChannels.Remove(listenerSignalRConnectionId);
        if (!_channels.Remove(channelId, out var channel))
        {
            return Array.Empty<RelayClientBinding>();
        }

        var clients = channel.Clients.Values.ToArray();
        foreach (var client in clients)
        {
            _clientsBySignalRConnection.Remove(client.SignalRConnectionId);
        }

        return clients;
    }

    private sealed class RelayChannel(string listenerSignalRConnectionId)
    {
        public string ListenerSignalRConnectionId { get; } = listenerSignalRConnectionId;

        public Dictionary<ConnectionId, RelayClientBinding> Clients { get; } = new();
    }
}

internal sealed record RelayClientBinding(
    string SignalRConnectionId,
    string ListenerSignalRConnectionId,
    ChannelId ChannelId,
    ConnectionId ConnectionId);
