using PartyGameKit.Core;
using PartyGameKit.Discovery.Lan;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.InMemory;
using PartyGameKit.Transport.Lan;
using PartyGameKit.Transport.SignalR;

var roomId = new RoomId("package-smoke-room");
var joinCode = new JoinCode("SMOKE1");

Console.WriteLine($"{roomId.Value}:{joinCode.Value}:v{ProtocolVersions.Current}");
Console.WriteLine(typeof(InMemoryGameTransport).FullName);
Console.WriteLine(typeof(LanWebSocketTransport).FullName);
Console.WriteLine(typeof(SignalRRoomTransport).FullName);
Console.WriteLine(typeof(UdpLanDiscoveryAdvertiser).FullName);
