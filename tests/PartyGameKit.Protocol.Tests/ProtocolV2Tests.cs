using System.Text.Json;
using PartyGameKit.Core;
using PartyGameKit.Protocol;

namespace PartyGameKit.Protocol.Tests;

public sealed class ProtocolV2Tests
{
    [Fact]
    public void ConnectRequest_SerializesNeutralPeerIdentity()
    {
        var envelope = PartyGameKitMessages.Create(
            ProtocolMessageTypes.ConnectRequest,
            "connect-1",
            new ConnectRequestPayload(new PeerId("peer-a")));

        var json = ProtocolJson.Serialize(envelope);

        Assert.Equal(
            "{\"type\":\"connection.connect.request\",\"protocolVersion\":2,\"messageId\":\"connect-1\",\"payload\":{\"peerId\":\"peer-a\"}}",
            json);
    }

    [Fact]
    public void ApplicationMessage_RoundTripsOpaqueConsumerPayload()
    {
        using var data = JsonDocument.Parse("{\"value\":42,\"label\":\"consumer-owned\"}");
        var envelope = PartyGameKitMessages.Create(
            ProtocolMessageTypes.ApplicationMessage,
            "application-1",
            new ApplicationMessagePayload("example.command", data.RootElement));
        var json = ProtocolJson.Serialize(envelope);

        var read = ProtocolJson.Read<ApplicationMessagePayload>(
            json,
            ProtocolMessageTypes.ApplicationMessage);

        Assert.True(read.IsSuccess);
        Assert.Equal("example.command", read.Message!.Payload.ApplicationType);
        Assert.Equal(42, read.Message.Payload.Data.GetProperty("value").GetInt32());
        Assert.Equal("consumer-owned", read.Message.Payload.Data.GetProperty("label").GetString());
    }

    [Fact]
    public void ResumeRequest_RoundTripsPeerAndCredentialOnly()
    {
        var envelope = PartyGameKitMessages.Create(
            ProtocolMessageTypes.ResumeRequest,
            "resume-1",
            new ResumeRequestPayload(new PeerId("peer-a"), "resume-token"));
        var json = ProtocolJson.Serialize(envelope);

        var read = ProtocolJson.Read<ResumeRequestPayload>(json, ProtocolMessageTypes.ResumeRequest);

        Assert.True(read.IsSuccess);
        Assert.Equal(new PeerId("peer-a"), read.Message!.Payload.PeerId);
        Assert.Equal("resume-token", read.Message.Payload.ResumeToken);
    }

    [Fact]
    public void OlderProtocolVersion_IsRejectedDeterministically()
    {
        const string json = "{\"type\":\"connection.connect.request\",\"protocolVersion\":1,\"messageId\":\"old-client\",\"payload\":{}}";

        var read = ProtocolJson.Read<ConnectRequestPayload>(json, ProtocolMessageTypes.ConnectRequest);

        Assert.False(read.IsSuccess);
        Assert.Equal(ProtocolReadError.ProtocolVersionMismatch, read.Error);
        Assert.Equal(1, read.ReceivedProtocolVersion);
    }

    [Fact]
    public void ConnectionRejectionCode_UsesStableWireValue()
    {
        var envelope = PartyGameKitMessages.Create(
            ProtocolMessageTypes.ConnectRejected,
            "reject-1",
            new ConnectRejectedPayload(ConnectionRejectionCode.InvalidRequest, "bad handshake"));

        var json = ProtocolJson.Serialize(envelope);

        Assert.Contains("\"code\":\"invalid-request\"", json, StringComparison.Ordinal);
    }
}
