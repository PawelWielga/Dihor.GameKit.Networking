import { PartyGameClient, createSignalRSocket, parseJoinDescriptor } from "/sdk/index.js";

const params = new URLSearchParams(location.search);
const role = params.get("role") === "shared-screen" ? "shared-screen" : "player";
const elements = {
  role: document.querySelector("#roleLabel"),
  status: document.querySelector("#status"),
  statusDot: document.querySelector("#statusDot"),
  identity: document.querySelector("#identity"),
  total: document.querySelector("#total"),
  players: document.querySelector("#players"),
  increment: document.querySelector("#increment"),
  privatePanel: document.querySelector("#privatePanel"),
  yourCount: document.querySelector("#yourCount"),
  descriptor: document.querySelector("#descriptor"),
};

const config = await fetch("/config.json", { cache: "no-store" }).then((response) => {
  if (!response.ok) throw new Error(`Config request failed: ${response.status}`);
  return response.json();
});
const descriptor = parseJoinDescriptor(config.joinDescriptor);
elements.descriptor.textContent = config.joinDescriptor;
elements.role.textContent = role === "shared-screen" ? "Shared TV/browser screen" : "Player controller";
elements.increment.hidden = role !== "player";
elements.privatePanel.hidden = role !== "player";

const client = new PartyGameClient({
  role,
  ...(descriptor.transport === "signalr" ? { webSocketFactory: createSignalRSocket } : {}),
});
client.on("state", (state) => {
  elements.status.textContent = state;
  elements.statusDot.classList.toggle("connected", state === "connected");
  elements.increment.disabled = state !== "connected";
});
client.on("error", (error) => {
  elements.status.textContent = error.message;
  elements.statusDot.classList.remove("connected");
});
client.on("snapshot", (snapshot) => {
  if (snapshot.target.kind === "public") {
    renderPublic(snapshot.state);
    return;
  }

  if (role === "player" && snapshot.state?.publicState && snapshot.state?.privateState) {
    renderPublic(snapshot.state.publicState);
    elements.yourCount.textContent = String(snapshot.state.privateState.yourCount ?? 0);
  }
});

elements.increment.addEventListener("click", () => {
  client.send(JSON.stringify({ type: "sample.counter.increment" }));
});

try {
  const session = await client.join(descriptor);
  elements.identity.textContent = session.playerId
    ? `playerId: ${session.playerId}`
    : `connectionId: ${session.connectionId}`;
} catch (error) {
  elements.status.textContent = error instanceof Error ? error.message : String(error);
}

function renderPublic(state) {
  elements.total.textContent = String(state?.total ?? 0);
  const players = Array.isArray(state?.players) ? state.players : [];
  if (players.length === 0) {
    elements.players.innerHTML = "<p>No players yet.</p>";
    return;
  }

  elements.players.replaceChildren(...players.map((player) => {
    const row = document.createElement("div");
    row.className = `player${player.connected ? "" : " disconnected"}`;
    const id = document.createElement("span");
    id.textContent = shortId(player.playerId);
    const count = document.createElement("strong");
    count.textContent = String(player.count ?? 0);
    row.append(id, count);
    return row;
  }));
}

function shortId(value) {
  const text = String(value ?? "player");
  return text.length <= 14 ? text : `${text.slice(0, 8)}…${text.slice(-5)}`;
}
