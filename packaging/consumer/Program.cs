using PartyGameKit.Core;
using PartyGameKit.Discovery.Lan;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.InMemory;
using PartyGameKit.Transport.Lan;

var channelId = new ChannelId("package-smoke-channel");
var descriptor = LanConnectionDescriptor.Create("127.0.0.1", 45678, channelId);

Console.WriteLine($"{descriptor.Transport}:{channelId.Value}:v{ProtocolVersions.Current}");
Console.WriteLine(typeof(InMemoryTransport).FullName);
Console.WriteLine(typeof(LanWebSocketTransport).FullName);
Console.WriteLine(typeof(UdpLanDiscoveryAdvertiser).FullName);
