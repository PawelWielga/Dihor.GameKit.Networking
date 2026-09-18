# Transient latest-value replay

Dihor.GameKit.Networking keeps reconnect-safe transient application replay separate from heartbeat and transport implementations.

The problem is intentionally narrow: a consumer may have one newest still-valid value that must survive a connection replacement. While disconnected, newer values replace older ones. After a replacement sender is bound, the newest buffered value is replayed immediately through the ordinary application-data path.

```text
connection/control plane
  connect / resume / heartbeat / disconnect

application data plane
  application.message
      -> optional LatestValueReplayBuffer
```

## Boundary

`LatestValueReplayBuffer` owns communication delivery mechanics only.

It does not know what a replay key or scope means. A consumer may use an interaction id, editing epoch, round instance, screen generation or any other caller-owned token.

It also does not know application payload semantics. The .NET implementation stores opaque bytes and the TypeScript implementation stores a caller-supplied message value.

Heartbeat remains a liveness/control message and never carries replayable application data.

## Latest-value semantics

One replay key stores at most one value:

```text
stage v1
stage v2
stage v3
disconnect
stage v4
stage v5
reconnect
=> replay v5
```

This is deliberately not a FIFO retry queue. It is intended for transient state where older values become obsolete as soon as a newer value exists.

Changing the scope on the same key replaces the previous value. A value from the old scope therefore cannot later replay into the new scope.

## Sender lifecycle

The replay buffer can be bound to an active `IMessageTransportClient` in .NET or a `ReplaySender<TMessage>` in TypeScript.

The lifecycle is:

1. bind the active sender after connect/resume;
2. stage latest values through the application path;
3. unbind as soon as connection replacement starts;
4. continue staging while unbound without touching the stale sender;
5. bind the replacement sender;
6. replay the newest still-valid buffered value immediately.

Binding a replacement sender cancels replay attempts against the previous sender. A stale sender may also call `UnbindSender(sender)` / `unbindSender(sender)` without detaching a newer replacement sender.

A staged value remains buffered after a locally successful send because transport completion is not a receiver acknowledgement. That retained latest value is what makes an ambiguous disconnect replay-safe. A send failure likewise leaves the newest buffered value intact so a later rebind can retry it.

The buffer therefore does not drain itself after send success. Consumers explicitly retire state when it is no longer valid.

`ClearLatest` / `clearLatest` removes one replay key. `InvalidateScope` / `invalidateScope` removes every buffered value in a caller-owned scope.

Clearing cannot retract a send that already started on a transport. It only guarantees that no later replay attempt is started from the removed buffer state.

## Message IDs and delivery semantics

Latest-value replay is **not exactly-once delivery**.

A connection can disappear after the sender handed bytes to the transport but before the caller knows whether the receiver observed them. For that reason, local send success does not remove replay state. The same staged message may be sent again on the replacement connection until the consumer explicitly clears or invalidates it.

For protocol-v2 `application.message` values:

- every new logical value should receive a new `messageId`;
- replay of that same staged value should reuse the same serialized envelope/bytes and therefore the same `messageId`;
- receiver-side bounded deduplication by `messageId` is tracked separately in [issue [25]](https://github.com/PawelWielga/Dihor.GameKit.Networking/issues/51).

Commands that require acknowledgement, retry-until-ACK or exactly-once-like business guarantees need a separate reliable-command protocol. They should not overload latest-value replay.

## .NET

Create the complete `application.message` bytes once, then stage those bytes:

```csharp
using System.Text;
using System.Text.Json;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;

using var replay = new LatestValueReplayBuffer();

var application = DihorGameKitNetworkingMessages.Create(
    ProtocolMessageTypes.ApplicationMessage,
    "message-draft-42-v7",
    new ApplicationMessagePayload(
        "consumer.transient-draft",
        JsonSerializer.SerializeToElement(new { value = "latest" })));

var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(application));

await replay.StageLatestAsync(
    key: "draft",
    scope: "interaction-42",
    message: bytes,
    cancellationToken);

await replay.BindSenderAsync(activeClient, cancellationToken);
```

When reconnect starts:

```csharp
replay.UnbindSender(activeClient);

// Staging while disconnected only replaces buffered state.
await replay.StageLatestAsync(
    "draft",
    "interaction-42",
    replacementApplicationBytes,
    cancellationToken);

await replay.BindSenderAsync(replacementClient, cancellationToken);
```

The replay buffer does not dispose transport clients. Transport ownership stays with the consumer/composition root.

## TypeScript

The browser SDK exposes the same lifecycle with `LatestValueReplayBuffer<TMessage>` and `ReplaySender<TMessage>`.

For protocol-v2 WebSocket traffic, stage the already serialized application message so reconnect reuses the same `messageId`:

```ts
const replay = new LatestValueReplayBuffer<string>();

const wireMessage = serializeMessage(createMessage(
  messageTypes.applicationMessage,
  "message-draft-42-v7",
  {
    applicationType: "consumer.transient-draft",
    data: { value: "latest" },
  },
));

await replay.stageLatest("draft", "interaction-42", wireMessage);

const sender = {
  send: (message: string) => client.sendRaw(message),
};

await replay.bindSender(sender);
```

On disconnect, call `replay.unbindSender(sender)`. A replacement LAN/WebSocket, WebRTC or relay adapter can then be bound to the same replay buffer.

The sender abstraction intentionally accepts an `AbortSignal`. Async adapters should honor it when possible so unbinding can stop pending work. A synchronous send that has already started cannot be retracted.

## Transport independence

Replay state lives above concrete transport implementations.

The same buffered value can survive a change from:

```text
LAN WebSocket -> WebRTC DataChannel -> SignalR relay
```

Automatic transport selection/fallback remains responsible only for choosing the replacement communication path. After selection completes, the consumer binds that replacement sender to the replay buffer.

No replay state is stored in LAN, WebRTC, SignalR or heartbeat code.

## Consumer responsibility

The consumer decides when to stage, what key to use, what scope/epoch means, when to clear/invalidate, whether received data is still valid for the domain and whether a stronger acknowledgement protocol is required.

Panstwa Miasta-specific answer drafts, rounds, category fingerprints, Flutter lifecycle, persistence, debounce, finalization and scoring remain outside Dihor.GameKit.Networking.
