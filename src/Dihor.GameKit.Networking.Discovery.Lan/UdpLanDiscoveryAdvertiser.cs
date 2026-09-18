using System.Net;
using System.Net.Sockets;
using System.Text;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;

namespace Dihor.GameKit.Networking.Discovery.Lan;

public sealed class UdpLanDiscoveryAdvertiser : IAsyncDisposable
{
    public const int DefaultDiscoveryPort = 45678;
    private readonly object _gate = new();
    private readonly ConnectionDescriptor _descriptor;
    private readonly int _discoveryPort;
    private readonly TimeSpan _interval;
    private readonly IReadOnlyList<IPAddress> _targetAddresses;
    private UdpClient? _client;
    private CancellationTokenSource? _runSource;
    private Task? _runTask;
    private bool _disposed;

    public UdpLanDiscoveryAdvertiser(
        ConnectionDescriptor descriptor,
        int discoveryPort = DefaultDiscoveryPort,
        TimeSpan? interval = null,
        IReadOnlyList<IPAddress>? targetAddresses = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (discoveryPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort), discoveryPort, "Discovery port must be between 1 and 65535.");
        }

        var resolvedInterval = interval ?? TimeSpan.FromSeconds(1);
        if (resolvedInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), resolvedInterval, "Announcement interval must be positive.");
        }

        _descriptor = descriptor;
        _discoveryPort = discoveryPort;
        _interval = resolvedInterval;
        _targetAddresses = targetAddresses is { Count: > 0 }
            ? targetAddresses.ToArray()
            : LanDiscoveryBroadcastAddressResolver.Resolve();
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runTask is not null;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null)
            {
                return Task.CompletedTask;
            }

            var client = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true,
            };
            var source = new CancellationTokenSource();
            _client = client;
            _runSource = source;
            _runTask = RunAsync(client, source.Token);
            return Task.CompletedTask;
        }
    }

    public async ValueTask StopAsync()
    {
        UdpClient? client;
        CancellationTokenSource? source;
        Task? runTask;
        lock (_gate)
        {
            client = _client;
            source = _runSource;
            runTask = _runTask;
            _client = null;
            _runSource = null;
            _runTask = null;
        }

        if (runTask is null)
        {
            return;
        }

        source!.Cancel();
        client!.Dispose();
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            source.Dispose();
        }
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
    }

    private async Task RunAsync(UdpClient client, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(
            DiscoveryEndpointAnnouncementCodec.Serialize(new DiscoveryEndpointAnnouncement(_descriptor)));
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var address in _targetAddresses)
            {
                try
                {
                    await client.SendAsync(
                        payload,
                        new IPEndPoint(address, _discoveryPort),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }

            await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
        }
    }
}
