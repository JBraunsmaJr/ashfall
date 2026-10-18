using Ashfall.Core;
using Ashfall.Session.Maps;
using Ashfall.Simulation;

namespace Ashfall.Session;

/// <summary>
/// Everything needed to start a match identically on every peer. Two modes for now: the M0 "proving
/// ground" (open map, wandering units, used to prove determinism) and the M1 skirmish (a duel map,
/// Bulwark bases, scripted AI players, played to victory). Map archetypes, sliders and AI tiers join
/// the spec in later milestones.
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

    /// <summary>The default proving-ground match for <paramref name="seed"/>.</summary>
    public static MatchSpec ProvingGround(ulong seed) => new() { Seed = seed };

    /// <summary>A 1v1 Bulwark skirmish on the duel map, both sides played by the scripted M1 AI.</summary>
    public static MatchSpec Skirmish(ulong seed) => new() { Seed = seed, Mode = MatchMode.Skirmish, Map = new MapBounds(DuelMap.Size, DuelMap.Size) };

    public MatchSpec WithPlayers(int players) => this with { Players = players };

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
        if (Mode == MatchMode.Skirmish && Players != 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Players), Players, "The M1 duel map has exactly two start positions.");
        }

        if (Mode == MatchMode.Skirmish && Controllers.Count != Players)
        {
            throw new ArgumentOutOfRangeException(nameof(Controllers), Controllers.Count, "A skirmish needs one controller per player.");
        }

        if (Controllers.Count(controller => controller == PlayerController.Human) > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Controllers), "Only one local human player is supported until multiplayer (M3).");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Players, 8, nameof(Players));
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
