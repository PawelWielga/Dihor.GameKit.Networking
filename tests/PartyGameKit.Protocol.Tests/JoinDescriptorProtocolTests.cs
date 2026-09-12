using PartyGameKit.Core;

namespace PartyGameKit.Protocol.Tests;

public sealed class JoinDescriptorProtocolTests
{
    private static readonly JoinDescriptor Descriptor = new(
        new RoomId("room-001"),
        new JoinCode("ROOM42"),
        "lan-websocket",
        "ws://192.168.1.20:5042/partygamekit",
        ProtocolVersions.Current);

    [Fact]
    public void JsonMatchesCanonicalFixtureAndRoundTrips()
    {
        var json = JoinDescriptorCodec.SerializeJson(Descriptor);

        Assert.Equal(ReadFixture("join-descriptor.json"), json);
        Assert.Equal(Descriptor, JoinDescriptorCodec.ParseJson(json));
    }

    [Fact]
    public void UriAndTextAreDeterministicAndRoundTrip()
    {
        const string expected = "partygamekit://join?protocolVersion=1&roomId=room-001&joinCode=ROOM42&transport=lan-websocket&endpoint=ws%3A%2F%2F192.168.1.20%3A5042%2Fpartygamekit";

        Assert.Equal(expected, JoinDescriptorCodec.SerializeUri(Descriptor));
        Assert.Equal(expected, JoinDescriptorCodec.SerializeText(Descriptor));
        Assert.Equal(Descriptor, JoinDescriptorCodec.ParseUri(expected));
        Assert.Equal(Descriptor, JoinDescriptorCodec.ParseText(expected));
        Assert.Equal(Descriptor, JoinDescriptorCodec.ParseText(JoinDescriptorCodec.SerializeJson(Descriptor)));
    }

    [Fact]
    public void DiscoveryAnnouncementMatchesCanonicalFixtureAndRoundTrips()
    {
        var payload = DiscoveryAnnouncementCodec.Serialize(new DiscoveryAnnouncement(Descriptor));

        Assert.Equal(ReadFixture("discovery-announcement.json"), payload);
        Assert.Equal(Descriptor, DiscoveryAnnouncementCodec.Parse(payload).Descriptor);
    }

    [Fact]
    public void IncompatibleDiscoveryVersionIsRejected()
    {
        var payload = ReadFixture("discovery-announcement.json")
            .Replace("\"protocolVersion\":1", "\"protocolVersion\":99", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => DiscoveryAnnouncementCodec.Parse(payload));
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", name)).TrimEnd();
}
