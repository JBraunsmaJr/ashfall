using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Keeps the Maw's population topped up (M5 spike; nests and the director take over later): each tick, up to
/// <see cref="HordeSettings.SpawnPerTick"/> creatures appear at random passable cells near the map edge, at least
/// <see cref="HordeSettings.MinimumDistance"/> cells' walk from any player structure. Does nothing in matches
/// without a horde. Draws only from the Maw's own random stream.
/// </summary>
public sealed class HordeSpawnSystem : ISystem
{
    // Random cells tried per creature before giving up on it for this tick.
    private const int Attempts = 8;

    public void Update(in TickContext context)
    {
        var simulation = context.Simulation;
        var maw = simulation.Maw;
        if (maw is null || !maw.Settings.EdgeSpawns || context.Tick < maw.Settings.FirstSpawnTick)
        {
            return;
        }

        var settings = maw.Settings;
        var alive = context.World.Store<Creature>().Count;
        var wanted = settings.Population - alive;
        if (wanted <= 0)
        {
            return;
        }

        if (wanted > settings.SpawnPerTick)
        {
            wanted = settings.SpawnPerTick;
        }

        var field = simulation.RefreshSwarmField();
        var map = context.Map;
        var random = maw.Random;
        var minimumCost = settings.MinimumDistance * FlowField.StraightCost;
        var jitter = Fix64.FromRatio(2, 5);
        for (var n = 0; n < wanted; n++)
        {
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                var cell = EdgeCell(map, random, settings.EdgeDepth);
                var cost = field.CostAt(cell);
                if (!map.IsPassable(cell) || cost < minimumCost)
                {
                    continue;
                }

                var position = cell.Center + new FixVec2(random.NextFix64(-jitter, jitter), random.NextFix64(-jitter, jitter));
                simulation.SpawnCreature(settings.CreatureType, position);
                maw.Spawned++;
                break;
            }
        }

        var now = context.World.Store<Creature>().Count;
        if (now > maw.PeakAlive)
        {
            maw.PeakAlive = now;
        }
    }

    // A uniformly chosen side, a position along it, and a depth inward from the edge.
    private static Cell EdgeCell(GridMap map, DeterministicRandom random, int depth)
    {
        var side = random.Next(4);
        var inward = random.Next(depth < map.Width && depth < map.Height ? depth : 1);
        return side switch
        {
            0 => new Cell(random.Next(map.Width), inward),
            1 => new Cell(map.Width - 1 - inward, random.Next(map.Height)),
            2 => new Cell(random.Next(map.Width), map.Height - 1 - inward),
            _ => new Cell(inward, random.Next(map.Height)),
        };
    }
}
