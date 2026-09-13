# State snapshots and ordering

## Boundary decision

PartyGameKit `0.2` base packages do **not** own a generic game snapshot model.

The historical v0.1 implementation combined useful ordering mechanics with product/game semantics such as authority, room identity, public/shared-screen state and private per-player projections. Those semantics are now consumer-owned.

See [Communication boundary](communication-boundary.md).

## Consumer-owned responsibilities

PartyBeam, Państwa Miasta or another application owns:

- authoritative application/game state;
- snapshot schema;
- public/private/player projection rules;
- snapshot target meaning;
- authority identity;
- latest-state restore policy after reconnect;
- whether snapshots exist at all.

Those snapshots may travel through PartyGameKit as opaque `application.message` data.

## Optional PartyGameKit utility

`MessageSequence` and `SequenceGate` provide a small monotonic ordering/deduplication helper that is communication-neutral.

For example:

```text
sequence 41 -> accept
sequence 42 -> accept
sequence 42 -> reject duplicate
sequence 40 -> reject stale
```

The helper does not require:

- `RoomId`;
- `PlayerId`;
- `AuthorityId`;
- public/private audience;
- game-state types.

A consumer is free not to use this helper if its application protocol has different ordering semantics.

## Reconnect

PartyGameKit resume restores communication identity/binding only.

After resume, a consumer may choose to send its newest application snapshot, replay events or reconstruct state another way. PartyGameKit does not impose state restoration semantics.

## Historical v0.1 API

The v0.1 snapshot surface included:

- `SnapshotAudience`;
- `SnapshotTarget`;
- `PublicStateProjection<T>`;
- `PlayerStateProjection<TPublic,TPrivate>`;
- `StateSnapshot<T>`;
- `PublishedSnapshotSet<...>`;
- `AuthoritativeSnapshotPublisher<...>`.

These types were removed from the active `0.2` base API. Their history remains available in the v0.1 tag, but new consumers should keep equivalent game/application state models in their own layer.
