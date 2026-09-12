using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;

namespace PartyGameKit.Transport.SignalR;

public sealed record SignalRGameClientMessage(
    ReadOnlyMemory<byte> Payload,
    bool IsClose = false,
    string? CloseDescription = null);

public sealed class SignalRGameClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly Channel<SignalRGameClientMessage> _messages = Channel.CreateUnbounded<SignalRGameClientMessage>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
        });
    private int _closeWritten;
    private int _disposed;

    private SignalRGameClient(HubConnection connection)
    {
        _connection = connection;
        _connection.On<string>("Receive", payloadBase64 =>
        {
            try
            {
                _messages.Writer.TryWrite(new SignalRGameClientMessage(Convert.FromBase64String(payloadBase64)));
            }
            catch (FormatException)
            {
                WriteClose("server-sent-invalid-base64");
            }
        });
        _connection.On<string>("Disconnect", reason => WriteClose(reason));
        _connection.Closed += exception =>
        {
            WriteClose(exception?.Message ?? "signalr-connection-closed");
            return Task.CompletedTask;
        };
    }

    public HubConnectionState State => _connection.State;

    public static async Task<SignalRGameClient> ConnectAsync(
        Uri endpoint,
        string handshakeJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(handshakeJson);
        if (endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("SignalR endpoint must use http or https.", nameof(endpoint));
        }

        var connection = new HubConnectionBuilder()
            .WithUrl(endpoint)
            .Build();
        var client = new SignalRGameClient(connection);
        try
        {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            await client.SendTextAsync(handshakeJson, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        SendBase64Async(Convert.ToBase64String(payload.Span), cancellationToken);

    public async ValueTask<SignalRGameClientMessage> ReceiveAsync(CancellationToken cancellationToken = default)
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
            await _connection.StopAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            WriteClose("client-disposed");
            _messages.Writer.TryComplete();
        }
    }

    private ValueTask SendTextAsync(string payload, CancellationToken cancellationToken) =>
        SendBase64Async(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)), cancellationToken);

    private async ValueTask SendBase64Async(string payloadBase64, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("SignalR client is not connected.");
        }

        await _connection.SendAsync("Send", payloadBase64, cancellationToken).ConfigureAwait(false);
    }

    private void WriteClose(string reason)
    {
        if (Interlocked.Exchange(ref _closeWritten, 1) == 0)
        {
            _messages.Writer.TryWrite(new SignalRGameClientMessage(ReadOnlyMemory<byte>.Empty, true, reason));
        }
    }
}
