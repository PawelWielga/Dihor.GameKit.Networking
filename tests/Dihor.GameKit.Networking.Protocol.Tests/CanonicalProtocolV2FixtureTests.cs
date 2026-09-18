using System.Text.Json;
using Dihor.GameKit.Networking.Protocol;

namespace Dihor.GameKit.Networking.Protocol.Tests;

public sealed class CanonicalProtocolV2FixtureTests
{
    [Theory]
    [InlineData("v2-connect-request.json", ProtocolMessageTypes.ConnectRequest)]
    [InlineData("v2-resume-request.json", ProtocolMessageTypes.ResumeRequest)]
    [InlineData("v2-heartbeat.json", ProtocolMessageTypes.Heartbeat)]
    [InlineData("v2-application-message.json", ProtocolMessageTypes.ApplicationMessage)]
    public void ControlAndApplicationFixturesUseProtocolV2(string fileName, string expectedType)
    {
        var json = File.ReadAllText(FixturePath(fileName));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(expectedType, root.GetProperty("type").GetString());
        Assert.Equal(ProtocolVersions.Current, root.GetProperty("protocolVersion").GetInt32());
    }

    [Fact]
    public void ConnectionDescriptorFixtureParsesWithNeutralCodec()
    {
        var json = File.ReadAllText(FixturePath("v2-connection-descriptor.json"));

        var descriptor = ConnectionDescriptorCodec.ParseJson(json);

        Assert.Equal(ProtocolVersions.Current, descriptor.ProtocolVersion);
        Assert.Equal("lan-websocket", descriptor.Transport);
        Assert.Equal("channel-a", descriptor.ChannelId?.Value);
    }

    [Fact]
    public void DiscoveryFixtureParsesWithNeutralCodec()
    {
        var json = File.ReadAllText(FixturePath("v2-discovery-announcement.json"));

        var announcement = DiscoveryEndpointAnnouncementCodec.Parse(json);

        Assert.Equal("channel-a", announcement.Descriptor.ChannelId?.Value);
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", fileName);
}
