using System.Net;
using PartyGameKit.Core;
using PartyGameKit.Discovery.Lan;
using PartyGameKit.Protocol;

namespace PartyGameKit.Discovery.Tests;

public sealed class UdpDiscoveryIntegrationTests
{
    [Fact]
    public async Task LoopbackDiscovery_FindsTechnicalConnectionDescriptor()
    {
        await using var listener = new UdpLanDiscoveryListener(
            IPAddress.Loopback,
            discoveryPort: 0,
            cleanupInterval: TimeSpan.FromMilliseconds(100));
        await listener.StartAsync();

        var descriptor = new ConnectionDescriptor(
            "lan-websocket",
            "ws://127.0.0.1:45678/partygamekit",
            ProtocolVersions.Current,
            new ChannelId("channel-a"));
        await using var advertiser = new UdpLanDiscoveryAdvertiser(
            descriptor,
            listener.BoundPort,
            interval: TimeSpan.FromMilliseconds(50),
            targetAddresses: [IPAddress.Loopback]);
        await advertiser.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var changes = listener.ReadChangesAsync(timeout.Token).GetAsyncEnumerator();
        DiscoveredEndpoint? found = null;
        while (await changes.MoveNextAsync())
        {
            found = changes.Current.FirstOrDefault(item => item.Descriptor == descriptor);
            if (found is not null)
            {
                break;
            }
        }

        Assert.NotNull(found);
        Assert.Equal(IPAddress.Loopback, found.SourceAddress);
    }

    [Fact]
    public void DirectDescriptor_DoesNotRequireDiscovery()
    {
        var descriptor = new ConnectionDescriptor(
            "lan-websocket",
            "ws://127.0.0.1:45678/partygamekit",
            ProtocolVersions.Current);

        var serialized = ConnectionDescriptorCodec.SerializeText(descriptor);
        var parsed = ConnectionDescriptorCodec.ParseText(serialized);

        Assert.Equal(descriptor, parsed);
    }
}
