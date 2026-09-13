# Neutral Communication Demo

`CommunicationDemo` is the reference sample for PartyGameKit's corrected communication-only boundary.

It deliberately has no `Player`, `Host`, `TV`, lobby, authority, score or game-state model. Two generic peers communicate over the real LAN WebSocket transport and treat application data as consumer-owned payloads.

The executable verifies, end to end:

- starting a real local LAN WebSocket listener;
- building and using a neutral `ConnectionDescriptor`;
- discovering that descriptor through UDP LAN discovery;
- connecting two generic peers;
- peer-to-host opaque `application.message` delivery;
- host-to-peer targeted delivery;
- broadcast delivery to both peers;
- replacing one socket/`ConnectionId` while preserving the same `PeerId` through `ConnectionContinuityCoordinator` resume;
- no duplicate logical peer after resume.

Run it from the repository root:

```bash
dotnet run --project samples/CommunicationDemo/PartyGameKit.Sample.CommunicationDemo.csproj
```

The demo binds only to loopback and uses an ephemeral TCP port, so it is deterministic enough to run in CI while still exercising the real Kestrel/WebSocket and UDP discovery implementations.

Product semantics belong above this sample. PartyBeam may map peers to a TV, pilot or controller; Państwa Miasta may map them to its own players and authoritative game state. PartyGameKit itself does not make those decisions.
