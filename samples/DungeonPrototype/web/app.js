import { PartyGameClient, parseJoinDescriptor } from "/sdk/index.js";

const params = new URLSearchParams(location.search);
const role = params.get("role") === "shared-screen" ? "shared-screen" : "player";
const elements = {
  role: document.querySelector("#roleLabel"),
  status: document.querySelector("#status"),
  statusDot: document.querySelector("#statusDot"),
  identity: document.querySelector("#identity"),
  turn: document.querySelector("#turn"),
  ap: document.querySelector("#ap"),
  map: document.querySelector("#map"),
  players: document.querySelector("#players"),
  descriptor: document.querySelector("#descriptor"),
  playerControls: document.querySelector("#playerControls"),
  characterPanel: document.querySelector("#characterPanel"),
  actionPanel: document.querySelector("#actionPanel"),
  character: document.querySelector("#character"),
  inventory: document.querySelector("#inventory"),
  attack: document.querySelector("#attack"),
  endTurn: document.querySelector("#endTurn"),
};

const config = await fetch("/config.json", { cache: "no-store" }).then((response) => {
  if (!response.ok) throw new Error(`Config request failed: ${response.status}`);
  return response.json();
});
const descriptor = parseJoinDescriptor(config.joinDescriptor);
elements.descriptor.textContent = config.joinDescriptor;
elements.role.textContent = role === "shared-screen" ? "Shared dungeon map" : "Private player controller";
elements.playerControls.hidden = role !== "player";

const client = new PartyGameClient({ role });
let latestPublic = null;
let latestPrivate = null;

client.on("state", (state) => {
  elements.status.textContent = state;
  elements.statusDot.classList.toggle("connected", state === "connected");
  updateControls();
});
client.on("error", (error) => {
  elements.status.textContent = error.message;
  elements.statusDot.classList.remove("connected");
  updateControls();
});
client.on("snapshot", (snapshot) => {
  if (snapshot.target.kind === "public") {
    latestPublic = snapshot.state;
  } else if (role === "player" && snapshot.state?.publicState && snapshot.state?.privateState) {
    latestPublic = snapshot.state.publicState;
    latestPrivate = snapshot.state.privateState;
  }
  render();
});

document.querySelectorAll("[data-character]").forEach((button) => {
  button.addEventListener("click", () => send({
    type: "sample.dungeon.select-character",
    character: button.dataset.character,
  }));
});
document.querySelectorAll("[data-move]").forEach((button) => {
  button.addEventListener("click", () => {
    const [dx, dy] = button.dataset.move.split(",").map(Number);
    send({ type: "sample.dungeon.move", dx, dy });
  });
});
elements.attack.addEventListener("click", () => send({ type: "sample.dungeon.attack" }));
elements.endTurn.addEventListener("click", () => send({ type: "sample.dungeon.end-turn" }));

try {
  const session = await client.join(descriptor);
  elements.identity.textContent = session.playerId
    ? `playerId: ${session.playerId}`
    : `connectionId: ${session.connectionId}`;
} catch (error) {
  elements.status.textContent = error instanceof Error ? error.message : String(error);
}

function send(command) {
  client.send(JSON.stringify(command));
}

function render() {
  if (!latestPublic) return;
  const active = latestPublic.activePlayerId;
  elements.turn.textContent = active ? shortId(active) : "Lobby";
  elements.ap.textContent = String(latestPublic.actionPoints ?? 0);
  renderMap(latestPublic);
  renderPlayers(latestPublic.players ?? []);

  if (role === "player" && latestPrivate) {
    elements.character.textContent = latestPrivate.character ?? "not selected";
    elements.inventory.textContent = latestPrivate.inventory?.length
      ? latestPrivate.inventory.join(", ")
      : "empty";
    elements.characterPanel.hidden = Boolean(latestPrivate.character);
    elements.actionPanel.hidden = latestPublic.phase !== "active";
  }
  updateControls();
}

function renderMap(state) {
  const cells = [];
  for (let y = 0; y < state.height; y += 1) {
    for (let x = 0; x < state.width; x += 1) {
      const cell = document.createElement("div");
      cell.className = "cell";
      const stack = document.createElement("div");
      stack.className = "stack";

      if (state.keyPosition?.x === x && state.keyPosition?.y === y) {
        stack.append(label("Key", "item"));
      }
      if (state.enemy?.alive && state.enemy.position.x === x && state.enemy.position.y === y) {
        stack.append(label(`Slime HP ${state.enemy.hitPoints}`, "enemy"));
      }
      for (const player of state.players ?? []) {
        if (player.position?.x === x && player.position?.y === y) {
          stack.append(label(`${player.character ?? "?"} ${shortId(player.playerId)}`));
        }
      }
      if (!stack.childNodes.length) stack.append(label("·", "muted"));
      cell.append(stack);
      cells.push(cell);
    }
  }
  elements.map.replaceChildren(...cells);
}

function renderPlayers(players) {
  if (!players.length) {
    elements.players.innerHTML = "<p>No players yet.</p>";
    return;
  }
  elements.players.replaceChildren(...players.map((player) => {
    const row = document.createElement("div");
    row.className = `player${player.connected ? "" : " disconnected"}`;
    const name = document.createElement("span");
    name.textContent = `${shortId(player.playerId)} · ${player.character ?? "choosing"}`;
    const position = document.createElement("strong");
    position.textContent = `(${player.position.x},${player.position.y})`;
    row.append(name, position);
    return row;
  }));
}

function updateControls() {
  const enabled = client.state === "connected" && Boolean(latestPrivate?.isYourTurn);
  document.querySelectorAll("[data-move], #attack, #endTurn").forEach((button) => {
    button.disabled = !enabled;
  });
}

function label(text, className = "") {
  const span = document.createElement("span");
  span.textContent = text;
  if (className) span.className = className;
  return span;
}

function shortId(value) {
  const text = String(value ?? "player");
  return text.length <= 15 ? text : `${text.slice(0, 8)}…${text.slice(-5)}`;
}
