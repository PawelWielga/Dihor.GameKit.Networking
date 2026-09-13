using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace PartyGameKit.Discovery.Lan;

public sealed class UdpLanDiscoveryListener : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IPAddress _bindAddress;
    private readonly int _configuredPort;
    private readonly TimeSpan _cleanupInterval;
    private readonly DiscoveredEndpointRegistry _registry;
    private readonly Channel<IReadOnlyList<DiscoveredEndpoint>> _changes =
        Channel.CreateUnbounded<IReadOnlyList<DiscoveredEndpoint>>();
    private UdpClient? _client;
    private CancellationTokenSource? _runSource;
    private Task? _receiveTask;
    private Task? _cleanupTask;
    private bool _disposed;

    public UdpLanDiscoveryListener(
        IPAddress? bindAddress = null,
        int discoveryPort = UdpLanDiscoveryAdvertiser.DefaultDiscoveryPort,
        TimeSpan? cleanupInterval = null,
        DiscoveredEndpointRegistry? registry = null)
    {
        if (discoveryPort is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort), discoveryPort, "Discovery port must be between 0 and 65535.");
        }

        var resolvedCleanupInterval = cleanupInterval ?? TimeSpan.FromSeconds(1);
        if (resolvedCleanupInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cleanupInterval), resolvedCleanupInterval, "Cleanup interval must be positive.");
        }

        _bindAddress = bindAddress ?? IPAddress.Any;
        _configuredPort = discoveryPort;
        _cleanupInterval = resolvedCleanupInterval;
        _registry = registry ?? new DiscoveredEndpointRegistry();
    }

    public int BoundPort { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _client is not null;
            }
        }
    }

    public IReadOnlyList<DiscoveredEndpoint> Endpoints => _registry.Endpoints;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client is not null)
            {
                return Task.CompletedTask;
            }

            var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            try
            {
                client.Client.Bind(new IPEndPoint(_bindAddress, _configuredPort));
            }
            catch
            {
                client.Dispose();
                throw;
            }

            BoundPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
            var source = new CancellationTokenSource();
            _client = client;
            _runSource = source;
            _receiveTask = ReceiveLoopAsync(client, source.Token);
            _cleanupTask = CleanupLoopAsync(source.Token);
            Publish();
            return Task.CompletedTask;
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<DiscoveredEndpoint>> ReadChangesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var endpoints in _changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return endpoints;
        }
    }

    public async ValueTask StopAsync()
    {
        UdpClient? client;
        CancellationTokenSource? source;
        Task? receiveTask;
        Task? cleanupTask;
        lock (_gate)
        {
            client = _client;
            source = _runSource;
            receiveTask = _receiveTask;
            cleanupTask = _cleanupTask;
            _client = null;
            _runSource = null;
            _receiveTask = null;
            _cleanupTask = null;
        }

        if (client is null)
        {
            return;
        }

        source!.Cancel();
        client.Dispose();
        await IgnoreCancellationAsync(receiveTask!).ConfigureAwait(false);
        await IgnoreCancellationAsync(cleanupTask!).ConfigureAwait(false);
        source.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
        _changes.Writer.TryComplete();
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var payload = Encoding.UTF8.GetString(result.Buffer);
                if (_registry.AddAnnouncement(payload, result.RemoteEndPoint.Address))
                {
                    Publish();
                }
            }
            catch (FormatException)
            {
            }
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_cleanupInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_registry.ExpireInactive())
                {
                    Publish();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Publish() => _changes.Writer.TryWrite(_registry.Endpoints);

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
