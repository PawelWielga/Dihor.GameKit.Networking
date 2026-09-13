using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using PartyGameKit.Core;

namespace PartyGameKit.Transport.SignalR;

public sealed record SignalRRelayClientMessage(
    ReadOnlyMemory<byte> Payload,
    bool IsClose = false,
    string? CloseDescription = null);

public sealed class SignalRRelayClient : IAsyncDisposable
{
    private readonly SignalRRelayOptions _options;
    private readonly HubConnection _connection;
    private readonly Channel<SignalRRelayClientMessage> _messages =
        Channel.CreateUnbounded<SignalRRelayClientMessage>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = false,
                SingleWriter = false,
            });
    private readonly List<IDisposable> _subscriptions = new();
    private int _closed;
    private int _disposed;

    private SignalRRelayClient(
        SignalRRelayOptions options,
        HubConnection connection,
        ConnectionId connectionId)
    {
        _options = options;
        _connection = connection;
        ConnectionId = connectionId;
        _subscriptions.Add(_connection.On<byte[]>(
            SignalRRelayMethods.ClientMessage,
            HandleMessage));
        _subscriptions.Add(_connection.On<string>(
            SignalRRelayMethods.ClientDisconnected,
            HandleDisconnected));
        _connection.Closed += HandleRelayClosedAsync;
    }

    public ConnectionId ConnectionId { get; }

    public static async Task<SignalRRelayClient> ConnectAsync(
        SignalRRelayOptions options,
        string handshakeJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(handshakeJson);

        var handshake = Encoding.UTF8.GetBytes(handshakeJson);
        if (handshake.Length > options.MaxMessageBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(handshakeJson),
                handshake.Length,
                $"Handshake exceeds the configured SignalR relay limit of {options.MaxMessageBytes} bytes.");
        }

        var connection = new HubConnectionBuilder()
            .WithUrl(
                options.Endpoint,
                httpOptions => options.ConfigureConnection?.Invoke(httpOptions))
            .Build();

        try
        {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            var rawConnectionId = await connection.InvokeAsync<string>(
                    SignalRRelayMethods.AttachClient,
                    options.ChannelId.Value,
                    handshake,
                    cancellationToken)
                .ConfigureAwait(false);
            var connectionId = new ConnectionId(rawConnectionId);
            return new SignalRRelayClient(options, connection, connectionId);
        }
        catch
        {
            try
            {
                if (connection.State != HubConnectionState.Disconnected)
                {
                    await connection.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
        ThrowIfTooLarge(payload);

        await _connection.InvokeAsync(
                SignalRRelayMethods.SendFromClient,
                ConnectionId.Value,
                payload.ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<SignalRRelayClientMessage> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return await _messages.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0 &&
                _connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await _connection.InvokeAsync(
                            SignalRRelayMethods.DetachClient,
                            ConnectionId.Value,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            if (_connection.State != HubConnectionState.Disconnected)
            {
                try
                {
                    await _connection.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
        finally
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _messages.Writer.TryComplete();
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void HandleMessage(byte[] payload)
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            return;
        }

        if (payload.Length > _options.MaxMessageBytes)
        {
            HandleDisconnected("message-too-large");
            return;
        }

        _messages.Writer.TryWrite(new SignalRRelayClientMessage(
            new ReadOnlyMemory<byte>(payload.ToArray())));
    }

    private void HandleDisconnected(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        _messages.Writer.TryWrite(new SignalRRelayClientMessage(
            ReadOnlyMemory<byte>.Empty,
            IsClose: true,
            CloseDescription: reason));
    }

    private Task HandleRelayClosedAsync(Exception? exception)
    {
        var reason = exception is null
            ? "relay-closed"
            : $"relay-closed: {exception.Message}";
        HandleDisconnected(reason);
        _messages.Writer.TryComplete();
        return Task.CompletedTask;
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _closed) != 0 || _connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("SignalR relay client is not connected.");
        }
    }

    private void ThrowIfTooLarge(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > _options.MaxMessageBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payload.Length,
                $"Message exceeds the configured SignalR relay limit of {_options.MaxMessageBytes} bytes.");
        }
    }
}
