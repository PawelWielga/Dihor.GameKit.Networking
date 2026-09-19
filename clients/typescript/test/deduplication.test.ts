import test from "node:test";
import assert from "node:assert/strict";
import { MessageIdDeduplicator } from "../src/index.js";

test("first peer/message-id pair is accepted and duplicate is rejected", () => {
  const deduplicator = new MessageIdDeduplicator();

  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), false);
  assert.equal(deduplicator.count, 1);
});

test("same message id from another peer is accepted", () => {
  const deduplicator = new MessageIdDeduplicator();

  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
  assert.equal(deduplicator.tryAccept("peer-b", "message-1"), true);
  assert.equal(deduplicator.count, 2);
});

test("different message ids from the same peer are accepted", () => {
  const deduplicator = new MessageIdDeduplicator();

  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
  assert.equal(deduplicator.tryAccept("peer-a", "message-2"), true);
  assert.equal(deduplicator.count, 2);
});

test("reconnect or transport fallback does not reset stable peer deduplication", () => {
  const deduplicator = new MessageIdDeduplicator();

  assert.equal(
    deduplicator.tryAccept("peer-stable", "replayed-message"),
    true,
  );

  // Connection/transport identity is deliberately absent from the key.
  assert.equal(
    deduplicator.tryAccept("peer-stable", "replayed-message"),
    false,
  );
});

test("capacity deterministically evicts the oldest accepted pair", () => {
  const deduplicator = new MessageIdDeduplicator({ capacity: 2 });

  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
  assert.equal(deduplicator.tryAccept("peer-a", "message-2"), true);
  assert.equal(deduplicator.tryAccept("peer-a", "message-3"), true);

  assert.equal(deduplicator.count, 2);

  // message-1 was the oldest pair and has been evicted.
  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
  assert.equal(deduplicator.count, 2);
});

test("expiration accepts the pair again and duplicate does not refresh retention", () => {
  let now = 0;
  const deduplicator = new MessageIdDeduplicator({
    capacity: 10,
    retentionMs: 10_000,
    now: () => now,
  });

  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);

  now = 9_000;
  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), false);

  now = 10_000;
  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
});

test("forgetPeer releases only that peer's entries", () => {
  const deduplicator = new MessageIdDeduplicator();

  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
  assert.equal(deduplicator.tryAccept("peer-a", "message-2"), true);
  assert.equal(deduplicator.tryAccept("peer-b", "message-1"), true);

  assert.equal(deduplicator.forgetPeer("peer-a"), 2);
  assert.equal(deduplicator.count, 1);

  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
  assert.equal(deduplicator.tryAccept("peer-b", "message-1"), false);
});

test("clear resets the whole deduplication window", () => {
  const deduplicator = new MessageIdDeduplicator();

  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
  deduplicator.clear();

  assert.equal(deduplicator.count, 0);
  assert.equal(deduplicator.tryAccept("peer-a", "message-1"), true);
});

test("invalid options and identifiers are rejected", () => {
  assert.throws(
    () => new MessageIdDeduplicator({ capacity: 0 }),
    /capacity/,
  );
  assert.throws(
    () => new MessageIdDeduplicator({ retentionMs: 0 }),
    /retentionMs/,
  );

  const deduplicator = new MessageIdDeduplicator();

  assert.throws(() => deduplicator.tryAccept(" ", "message-1"), /peerId/);
  assert.throws(() => deduplicator.tryAccept("peer-a", " "), /messageId/);
});
