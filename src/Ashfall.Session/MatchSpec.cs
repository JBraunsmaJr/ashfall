using Ashfall.Core;
using Ashfall.MapGen;
using Ashfall.Session.Maps;
using Ashfall.Simulation;

namespace Ashfall.Session;

/// <summary>
/// Everything needed to start a match identically on every peer. Two modes for now: the M0 "proving
/// ground" (open map, wandering units, used to prove determinism) and the skirmish (2–4 Bulwark
/// players on a hand-made or generated map, any mix of people and AIs, in teams, played to victory).
/// AI tiers join the spec in a later milestone.
/// </summary>
public sealed record MatchSpec
{
    /// <summary>Default skirmish tick limit: 60 minutes of game time. A match still running then is a failure.</summary>
    public const long SkirmishTickLimit = 54_000;

    public required ulong Seed { get; init; }

    public MatchMode Mode { get; init; } = MatchMode.ProvingGround;

    public MapBounds Map { get; init; } = new(64, 64);

    public int Players { get; init; } = 2;

    public int UnitsPerPlayer { get; init; } = 100;

    /// <summary>Unit speed in cells per tick. Default 0.1 cells/tick = 1.5 cells per second.</summary>
    public Fix64 UnitSpeedPerTick { get; init; } = Fix64.FromRatio(1, 10);

    /// <summary>Ticks between each player's random orders. 0 disables them.</summary>
    public int OrderIntervalTicks { get; init; } = 15;

    public int OrdersPerInterval { get; init; } = 5;

    /// <summary>Skirmish only: who plays each side. Default: two scripted AIs.</summary>
    public IReadOnlyList<PlayerController> Controllers { get; init; } = [PlayerController.ScriptedAi, PlayerController.ScriptedAi];

    /// <summary>Skirmish only: each player's team. Players on one team share vision and win together.</summary>
    public IReadOnlyList<int> Teams { get; init; } = [0, 1];

    /// <summary>
    /// Lockstep input delay in ticks: a command issued on tick T runs on tick T + delay, and remote
    /// seats' turns before tick <see cref="InputDelay"/> are empty. 0 outside network matches.
    /// </summary>
    public int InputDelay { get; init; }

    /// <summary>Skirmish only: the procedural map to play on, or null for the hand-made duel/quad map.</summary>
    public MapSettings? GeneratedMap { get; init; }

    /// <summary>The default proving-ground match for <paramref name="seed"/>.</summary>
    public static MatchSpec ProvingGround(ulong seed) => new() { Seed = seed };

    /// <summary>A 1v1 Bulwark skirmish on the duel map, both sides played by the scripted M1 AI.</summary>
    public static MatchSpec Skirmish(ulong seed) => Skirmish(seed, 2);

    /// <summary>A free-for-all Bulwark skirmish for <paramref name="players"/> scripted AIs (2–4).</summary>
    public static MatchSpec Skirmish(ulong seed, int players)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, SkirmishMaps.MaxPlayers);
        var size = SkirmishMaps.SizeFor(players);
        return new MatchSpec
        {
            Seed = seed,
            Mode = MatchMode.Skirmish,
            Map = new MapBounds(size, size),
            Players = players,
            Controllers = Enumerable.Repeat(PlayerController.ScriptedAi, players).ToArray(),
            Teams = Enumerable.Range(0, players).ToArray(),
        };
    }

    public MatchSpec WithPlayers(int players) => this with { Players = players };

    /// <summary>Plays the skirmish on a generated map (its size comes from the settings).</summary>
    public MatchSpec WithGeneratedMap(MapSettings? settings)
    {
        if (settings is null)
        {
            var size = SkirmishMaps.SizeFor(Players);
            return this with { GeneratedMap = null, Map = new MapBounds(size, size) };
        }

        var generated = MapGenerator.SizeOf(MapGenerator.ApplyLimits(settings, out _));
        return this with { GeneratedMap = settings, Map = new MapBounds(generated, generated) };
    }

    /// <summary>Sets every player's team, in seat order.</summary>
    public MatchSpec WithTeams(params int[] teams) => this with { Teams = teams.ToArray() };

    /// <summary>Sets who plays <paramref name="player"/> in a skirmish.</summary>
    public MatchSpec WithController(int player, PlayerController controller)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, Controllers.Count);
        var controllers = Controllers.ToArray();
        controllers[player] = controller;
        return this with { Controllers = controllers };
    }

    /// <summary>The player seat controlled by a person, or -1 if every side is an AI.</summary>
    public int HumanPlayer
    {
        get
        {
            for (var player = 0; player < Controllers.Count; player++)
            {
                if (Controllers[player] == PlayerController.Human)
                {
                    return player;
                }
            }

            return -1;
        }
    }

    public MatchSpec WithUnitsPerPlayer(int units) => this with { UnitsPerPlayer = units };

    public MatchSpec WithMap(int width, int height) => this with { Map = new MapBounds(width, height) };

    public MatchSpec WithOrders(int intervalTicks, int ordersPerInterval) =>
        this with { OrderIntervalTicks = intervalTicks, OrdersPerInterval = ordersPerInterval };

    /// <summary>Throws if the spec cannot start a match.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Players, 1, nameof(Players));
        if (Mode == MatchMode.Skirmish)
        {
            if (Players < 2 || Players > SkirmishMaps.MaxPlayers)
            {
                throw new ArgumentOutOfRangeException(nameof(Players), Players, $"Skirmish maps have 2 to {SkirmishMaps.MaxPlayers} start positions.");
            }

            var size = GeneratedMap is { } generated ? MapGenerator.SizeOf(MapGenerator.ApplyLimits(generated, out _)) : SkirmishMaps.SizeFor(Players);
            if (Map.Width != size || Map.Height != size)
            {
                throw new ArgumentOutOfRangeException(nameof(Map), Map, "A skirmish's map size is set by its map (and, for hand-made maps, its player count).");
            }

            if (Controllers.Count != Players)
            {
                throw new ArgumentOutOfRangeException(nameof(Controllers), Controllers.Count, "A skirmish needs one controller per player.");
            }

            if (Teams.Count != Players)
            {
                throw new ArgumentOutOfRangeException(nameof(Teams), Teams.Count, "A skirmish needs one team per player.");
            }

            if (Teams.Distinct().Count() < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(Teams), "A skirmish needs at least two teams.");
            }
        }

        if (Controllers.Count(controller => controller == PlayerController.Human) > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Controllers), "Only one person can play at this machine; network players are Remote seats.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Players, 8, nameof(Players));
        ArgumentOutOfRangeException.ThrowIfNegative(InputDelay, nameof(InputDelay));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(InputDelay, 30, nameof(InputDelay));
        ArgumentOutOfRangeException.ThrowIfNegative(UnitsPerPlayer, nameof(UnitsPerPlayer));
        ArgumentOutOfRangeException.ThrowIfNegative(OrderIntervalTicks, nameof(OrderIntervalTicks));
        ArgumentOutOfRangeException.ThrowIfNegative(OrdersPerInterval, nameof(OrdersPerInterval));
        if (Map.Width <= 0 || Map.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Map), Map, "Map must be at least one cell in each direction.");
        }

        if (UnitSpeedPerTick <= Fix64.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(UnitSpeedPerTick), UnitSpeedPerTick, "Units must move.");
        }
    }
}
