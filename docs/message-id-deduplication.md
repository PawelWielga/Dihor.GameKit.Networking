# Message-id deduplication

Reconnect and transport replacement can make delivery ambiguous. A sender may have handed an `application.message` to the old transport without knowing whether the receiver processed it. Replaying the same logical message on the replacement connection is therefore useful, but the receiver needs a way to recognize that it has already processed that logical message.

`MessageIdDeduplicator` provides that communication-level primitive.

## What it answers

The deduplicator answers one question:

> Has this stable peer's logical message id already been accepted inside the current bounded window?

Its key is:

```text
(PeerId, messageId)
```

It deliberately does **not** use `ConnectionId` or transport identity. The same `PeerId` may reconnect on a new `ConnectionId`, or automatic connectivity may replace WebRTC with SignalR, while a replay of the same `messageId` still remains a duplicate.

The same `messageId` from another `PeerId` is a different logical sender and is accepted independently.

## Bounded state

The default window is bounded in two ways:

- maximum **1024** remembered peer/message-id pairs;
- default retention of **5 minutes** from the first accepted observation.

Both values are configurable.

When capacity is exceeded, the oldest accepted pair is evicted first. When retention expires, the pair can be accepted again. Observing a duplicate does not extend its retention window.

Choose retention so it covers the reconnect/replay ambiguity window that matters to the consumer. If a consumer allows a peer to resume for longer than the deduplicator retention, it should configure a longer retention.

## Peer cleanup

Call `ForgetPeer` / `forgetPeer` when stable continuity for a peer is deliberately discarded, including when the reconnect window expires and that `PeerId` will no longer resume its previous logical connection.

This releases all deduplication entries owned by that peer immediately.

`Clear` / `clear` resets the entire window.

## Different from sequence ordering

`SequenceGate` and `MessageIdDeduplicator` solve different problems.

```text
SequenceGate
  "Is this sequence newer than the last one I accepted?"

MessageIdDeduplicator
  "Have I already processed this exact logical message id from this peer?"
```

A sequence gate can reject stale state even when every message has a different id. A message-id deduplicator can reject the same replayed command/value even when no sequence number exists.

Consumers may use one, both or neither depending on their application protocol.

## Not acknowledgements or exactly-once delivery

Deduplication does not tell the sender that the receiver processed a message. It also does not retry a message and cannot create true exactly-once delivery.

For replayable latest-value state:

1. create a new `messageId` for each new logical value;
2. replay the same staged value with the same `messageId`;
3. call `TryAccept` / `tryAccept` before processing it on the receiver;
4. ignore the payload when the result is false.

Commands that require retry-until-ACK or application-level confirmation need a separate reliable-command/acknowledgement mechanism.

## .NET

```csharp
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;

var deduplicator = new MessageIdDeduplicator(
    capacity: 2048,
    retention: TimeSpan.FromMinutes(10));

ProtocolEnvelope<ApplicationMessagePayload> message = received;
PeerId peerId = stablePeerId;

if (!deduplicator.TryAccept(peerId, message.MessageId))
{
    return; // duplicate replay: already processed inside the window
}

ProcessApplicationMessage(message);
```

When continuity for the peer expires:

```csharp
deduplicator.ForgetPeer(peerId);
```

The implementation is thread-safe and uses the runtime `TimeProvider`, which also makes expiration deterministic in tests.

## TypeScript

```ts
import { MessageIdDeduplicator } from "@dihor/gamekit-networking";

const deduplicator = new MessageIdDeduplicator({
  capacity: 2048,
  retentionMs: 10 * 60 * 1000,
});

if (!deduplicator.tryAccept(peerId, envelope.messageId)) {
  return; // duplicate replay
}

processApplicationMessage(envelope);
```

When continuity for the peer expires:

```ts
deduplicator.forgetPeer(peerId);
```

## Transport independence

Deduplication lives above concrete transports. The same window can remain active across:

```text
LAN WebSocket -> replacement LAN connection
WebRTC DataChannel -> SignalR relay
SignalR relay -> LAN WebSocket
```

The utility contains no player, lobby, round, host or game semantics. The consumer decides which received application messages should use it and when peer continuity has ended.
