using System.Text.Json.Serialization;
using PartyGameKit.Core;

namespace PartyGameKit.Sample.DungeonPrototype;

public sealed record DungeonPosition(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

public sealed record DungeonPlayerView(
    [property: JsonPropertyName("playerId")] string PlayerId,
    [property: JsonPropertyName("character")] string? Character,
    [property: JsonPropertyName("position")] DungeonPosition Position,
    [property: JsonPropertyName("connected")] bool Connected);

public sealed record DungeonEnemyView(
    [property: JsonPropertyName("position")] DungeonPosition Position,
    [property: JsonPropertyName("hitPoints")] int HitPoints,
    [property: JsonPropertyName("alive")] bool Alive);

public sealed record DungeonPublicState(
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("activePlayerId")] string? ActivePlayerId,
    [property: JsonPropertyName("actionPoints")] int ActionPoints,
    [property: JsonPropertyName("enemy")] DungeonEnemyView Enemy,
    [property: JsonPropertyName("keyPosition")] DungeonPosition? KeyPosition,
    [property: JsonPropertyName("players")] IReadOnlyList<DungeonPlayerView> Players);

public sealed record DungeonPrivateState(
    [property: JsonPropertyName("character")] string? Character,
    [property: JsonPropertyName("inventory")] IReadOnlyList<string> Inventory,
    [property: JsonPropertyName("isYourTurn")] bool IsYourTurn,
    [property: JsonPropertyName("actionPoints")] int ActionPoints);

public sealed record DungeonPlayerProjection(
    [property: JsonPropertyName("publicState")] DungeonPublicState PublicState,
    [property: JsonPropertyName("privateState")] DungeonPrivateState PrivateState);

internal sealed class DungeonGame
{
    public const int Width = 4;
    public const int Height = 2;
    public const int ActionPointsPerTurn = 2;

    private readonly Dictionary<PlayerId, DungeonPlayerState> _players = new();
    private readonly List<PlayerId> _turnOrder = new();
    private readonly DungeonPosition _enemyPosition = new(3, 0);
    private DungeonPosition? _keyPosition = new(1, 0);
    private int _enemyHitPoints = 2;
    private int _activePlayerIndex = -1;
    private int _actionPoints;

    public void EnsurePlayer(PlayerId playerId)
    {
        if (_players.ContainsKey(playerId))
        {
            return;
        }

        var spawn = new DungeonPosition(0, _players.Count % Height);
        _players.Add(playerId, new DungeonPlayerState(spawn));
        _turnOrder.Add(playerId);
    }

    public void RemovePlayer(PlayerId playerId)
    {
        var wasActive = ActivePlayerId == playerId;
        if (!_players.Remove(playerId))
        {
            return;
        }

        var removedIndex = _turnOrder.IndexOf(playerId);
        if (removedIndex >= 0)
        {
            _turnOrder.RemoveAt(removedIndex);
            if (_activePlayerIndex > removedIndex)
            {
                _activePlayerIndex--;
            }
        }

        if (_turnOrder.Count == 0)
        {
            _activePlayerIndex = -1;
            _actionPoints = 0;
            return;
        }

        if (wasActive)
        {
            _activePlayerIndex %= _turnOrder.Count;
            _actionPoints = ActionPointsPerTurn;
        }
        else if (_activePlayerIndex >= _turnOrder.Count)
        {
            _activePlayerIndex = 0;
        }
    }

    public bool SelectCharacter(PlayerId playerId, string character)
    {
        if (!_players.TryGetValue(playerId, out var player) || player.Character is not null)
        {
            return false;
        }

        var normalized = character.Trim().ToLowerInvariant();
        if (normalized is not ("scout" or "guardian"))
        {
            return false;
        }

        player.Character = normalized;
        TryStartGame();
        return true;
    }

    public bool Move(PlayerId playerId, int deltaX, int deltaY)
    {
        if (!CanAct(playerId) || Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
        {
            return false;
        }

        var player = _players[playerId];
        var destination = new DungeonPosition(player.Position.X + deltaX, player.Position.Y + deltaY);
        if (destination.X < 0 || destination.X >= Width || destination.Y < 0 || destination.Y >= Height)
        {
            return false;
        }

        if (_enemyHitPoints > 0 && destination == _enemyPosition)
        {
            return false;
        }

        player.Position = destination;
        if (_keyPosition == destination)
        {
            player.Inventory.Add("Rusty Key");
            _keyPosition = null;
        }

        SpendActionPoint();
        return true;
    }

    public bool Attack(PlayerId playerId)
    {
        if (!CanAct(playerId) || _enemyHitPoints <= 0)
        {
            return false;
        }

        var player = _players[playerId];
        var distance = Math.Abs(player.Position.X - _enemyPosition.X) +
            Math.Abs(player.Position.Y - _enemyPosition.Y);
        if (distance != 1)
        {
            return false;
        }

        _enemyHitPoints = Math.Max(0, _enemyHitPoints - 1);
        SpendActionPoint();
        return true;
    }

    public bool EndTurn(PlayerId playerId)
    {
        if (!CanAct(playerId))
        {
            return false;
        }

        AdvanceTurn();
        return true;
    }

    public DungeonPublicState CreatePublicState(Func<PlayerId, bool> isConnected)
    {
        ArgumentNullException.ThrowIfNull(isConnected);
        var players = _turnOrder
            .Where(_players.ContainsKey)
            .Select(playerId =>
            {
                var player = _players[playerId];
                return new DungeonPlayerView(
                    playerId.Value,
                    player.Character,
                    player.Position,
                    isConnected(playerId));
            })
            .ToArray();

        return new DungeonPublicState(
            ActivePlayerId is null ? "lobby" : "active",
            Width,
            Height,
            ActivePlayerId?.Value,
            _actionPoints,
            new DungeonEnemyView(_enemyPosition, _enemyHitPoints, _enemyHitPoints > 0),
            _keyPosition,
            players);
    }

    public IReadOnlyDictionary<PlayerId, DungeonPrivateState> CreatePrivateStates()
    {
        return _players.ToDictionary(
            entry => entry.Key,
            entry => new DungeonPrivateState(
                entry.Value.Character,
                entry.Value.Inventory.ToArray(),
                ActivePlayerId == entry.Key,
                ActivePlayerId == entry.Key ? _actionPoints : 0));
    }

    private PlayerId? ActivePlayerId =>
        _activePlayerIndex >= 0 && _activePlayerIndex < _turnOrder.Count
            ? _turnOrder[_activePlayerIndex]
            : null;

    private bool CanAct(PlayerId playerId) =>
        ActivePlayerId == playerId && _actionPoints > 0;

    private void TryStartGame()
    {
        if (_activePlayerIndex >= 0 || _players.Count < 2 || _players.Values.Any(player => player.Character is null))
        {
            return;
        }

        _activePlayerIndex = 0;
        _actionPoints = ActionPointsPerTurn;
    }

    private void SpendActionPoint()
    {
        _actionPoints--;
        if (_actionPoints == 0)
        {
            AdvanceTurn();
        }
    }

    private void AdvanceTurn()
    {
        if (_turnOrder.Count == 0)
        {
            _activePlayerIndex = -1;
            _actionPoints = 0;
            return;
        }

        _activePlayerIndex = (_activePlayerIndex + 1) % _turnOrder.Count;
        _actionPoints = ActionPointsPerTurn;
    }

    private sealed class DungeonPlayerState(DungeonPosition position)
    {
        public string? Character { get; set; }

        public DungeonPosition Position { get; set; } = position;

        public List<string> Inventory { get; } = [];
    }
}
