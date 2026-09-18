# Neutral Communication Demo

`CommunicationDemo` is the reference sample for Dihor.GameKit.Networking's corrected communication-only boundary.

It deliberately has no `Player`, `Host`, `TV`, lobby, authority, score or game-state model. Two generic peers run the same protocol-v2/application-message scenario over direct LAN WebSocket and backend-assisted SignalR relay transports.

The executable verifies, end to end:

- starting a real local LAN WebSocket listener;
- building, serializing, parsing and using a neutral `ConnectionDescriptor`;
- discovering that descriptor through UDP LAN discovery;
- starting a real in-process ASP.NET Core SignalR relay endpoint;
- using `SignalRRelayTransport` behind the same `IMessageTransport` host loop;
- connecting two generic peers over each transport;
- peer-to-host opaque `application.message` delivery;
- host-to-peer targeted delivery;
- broadcast delivery to both peers;
- replacing one transport connection/`ConnectionId` while preserving the same `PeerId` through `ConnectionContinuityCoordinator` resume;
- no duplicate logical peer after resume.

Run both real transport scenarios from the repository root:

```bash
dotnet run --project samples/CommunicationDemo/Dihor.GameKit.Networking.Sample.CommunicationDemo.csproj
```

Run only LAN:

```bash
dotnet run --project samples/CommunicationDemo/Dihor.GameKit.Networking.Sample.CommunicationDemo.csproj -- lan
```

Run only SignalR:

```bash
dotnet run --project samples/CommunicationDemo/Dihor.GameKit.Networking.Sample.CommunicationDemo.csproj -- signalr
```

The demo binds only to loopback and uses ephemeral ports, so it is deterministic enough to run in CI while still exercising real Kestrel/WebSocket, UDP discovery and SignalR implementations.

The relay instance used by the sample is deliberately local and ephemeral. Production deployment, TLS and authentication/authorization requirements are described in `docs/signalr-relay.md`.

Product semantics belong above this sample. PartyBeam may map peers to a TV, pilot or controller; Państwa Miasta may map them to its own players and authoritative game state. Dihor.GameKit.Networking itself does not make those decisions.
