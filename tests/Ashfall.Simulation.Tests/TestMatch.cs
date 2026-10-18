using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;

namespace Ashfall.Simulation.Tests;

/// <summary>Small two-player Bulwark matches on an open map, for testing one rule at a time.</summary>
internal static class TestMatch
{
    public static GameContent Content => GameContent.Standard;

    public static int Structure(string id) => Content.StructureIndex(id);

    public static int Unit(string id) => Content.UnitIndex(id);

    /// <summary>Two players on opposing teams, open 32x32 map, no structures yet.</summary>
    public static GameSimulation Create(int size = 32, bool victory = false, GridMap? map = null) =>
        new(7, map ?? new GridMap(size, size), Content,
            [new PlayerSetup(0, BulwarkContent.FactionId), new PlayerSetup(1, BulwarkContent.FactionId)],
            victory);

    /// <summary>A match where player 0 has a completed Construction Yard at (2, 2).</summary>
    public static GameSimulation WithYard(int size = 32, bool victory = false, GridMap? map = null)
    {
        var sim = Create(size, victory, map);
        sim.PlaceStructure(0, Structure(BulwarkContent.ConstructionYard), new Cell(2, 2));
        return sim;
    }

    public static void Run(GameSimulation sim, int ticks, params Command[] firstTickCommands)
    {
        sim.Step(firstTickCommands);
        for (var i = 1; i < ticks; i++)
        {
            sim.Step([]);
        }
    }

    /// <summary>Steps until the condition holds; fails the test if it does not within the limit.</summary>
    public static int RunUntil(GameSimulation sim, Func<GameSimulation, bool> condition, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            if (condition(sim))
            {
                return i;
            }

            sim.Step([]);
        }

        Assert.Fail($"Condition not met within {maxTicks} ticks.");
        return -1;
    }

    public static int Count<T>(GameSimulation sim, int playerId, Func<T, bool>? filter = null)
        where T : unmanaged, IStateHashable
    {
        var count = 0;
        var store = sim.World.Store<T>();
        foreach (var index in store.EntityIndices)
        {
            if (sim.World.Store<Owner>().Get(index).PlayerId == playerId && (filter is null || filter(store.Get(index))))
            {
                count++;
            }
        }

        return count;
    }

    public static int CountUnits(GameSimulation sim, int playerId, string unitId) =>
        Count<UnitInfo>(sim, playerId, info => info.UnitType == Unit(unitId));

    public static FixVec2 PositionOf(GameSimulation sim, Entity entity) => sim.World.Get<Position>(entity).Value;
}
