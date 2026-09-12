# State snapshots and ordering

## Boundary decision

PartyGameKit base packages do **not** own a generic game snapshot model.

The historical v0.1 implementation combined useful ordering mechanics with product/game semantics such as authority, room identity, public/shared-screen state and private per-player projections. Issue `[15]` moves those semantics to consumers.

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

Those snapshots may travel through PartyGameKit as opaque application messages.

## Optional PartyGameKit utility

A small monotonic ordering/deduplication helper may remain because it is communication-neutral.

For example:

```text
sequence 41 -> accept
sequence 42 -> accept
sequence 42 -> reject duplicate
sequence 40 -> reject stale
```

The helper must not require:

- `RoomId`;
- `PlayerId`;
- `AuthorityId`;
- public/private audience;
- game state types.

`SnapshotSequence` / `SnapshotSequenceGate` may therefore become a general `MessageSequence` / `SequenceGate`-style optional utility in `[16]`.

## Reconnect

PartyGameKit resume restores communication identity/binding only.

After resume, a consumer may choose to send its newest application snapshot or reconstruct state another way. PartyGameKit does not impose state restoration semantics.

## Historical v0.1 API

The following current preview types are scheduled to move out of the base API:

- `SnapshotAudience`;
- `SnapshotTarget`;
- `PublicStateProjection<T>`;
- `PlayerStateProjection<TPublic,TPrivate>`;
- `StateSnapshot<T>`;
- `PublishedSnapshotSet<...>`;
- `AuthoritativeSnapshotPublisher<...>`.

Only a neutral ordering/deduplication primitive may survive after `[16]`.