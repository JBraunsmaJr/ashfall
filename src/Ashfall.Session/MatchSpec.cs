using Ashfall.Core;
using Ashfall.Simulation;

namespace Ashfall.Session;

/// <summary>
/// Everything needed to start a match identically on every peer. In M0 this describes the
/// "proving ground": an open map with wandering units, used to prove determinism. Factions, map
/// archetypes, sliders and AI tiers join the spec in later milestones.
/// </summary>
public sealed record MatchSpec
{
    public required ulong Seed { get; init; }

    public MapBounds Map { get; init; } = new(64, 64);

    public int Players { get; init; } = 2;

    public int UnitsPerPlayer { get; init; } = 100;

    /// <summary>Unit speed in cells per tick. Default 0.1 cells/tick = 1.5 cells per second.</summary>
    public Fix64 UnitSpeedPerTick { get; init; } = Fix64.FromRatio(1, 10);

    /// <summary>Ticks between each player's random orders. 0 disables them.</summary>
    public int OrderIntervalTicks { get; init; } = 15;

    public int OrdersPerInterval { get; init; } = 5;

    /// <summary>The default proving-ground match for <paramref name="seed"/>.</summary>
    public static MatchSpec ProvingGround(ulong seed) => new() { Seed = seed };

    public MatchSpec WithPlayers(int players) => this with { Players = players };

    public MatchSpec WithUnitsPerPlayer(int units) => this with { UnitsPerPlayer = units };

    public MatchSpec WithMap(int width, int height) => this with { Map = new MapBounds(width, height) };

    public MatchSpec WithOrders(int intervalTicks, int ordersPerInterval) =>
        this with { OrderIntervalTicks = intervalTicks, OrdersPerInterval = ordersPerInterval };

    /// <summary>Throws if the spec cannot start a match.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Players, 1, nameof(Players));
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
