using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using Ashfall.Simulation.Systems;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

public class SpatialTests
{
    [Fact]
    public void The_grid_buckets_units_and_creatures_by_cell_but_not_structures()
    {
        var sim = Create();
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(4, 7).Center);
        var other = sim.SpawnUnit(GameSimulation.MawPlayerId, Unit(BulwarkContent.Rifleman), new FixVec2(Fix64.FromInt(4), Fix64.FromRatio(15, 2)));
        sim.PlaceStructure(1, Structure(BulwarkContent.Barracks), new Cell(10, 10));

        sim.Spatial.Rebuild(sim.World);

        Assert.Equal(2, sim.Spatial.Count);
        Assert.Equal([rifleman.Index, other.Index], sim.Spatial.At(4, 7).ToArray());
        Assert.True(sim.Spatial.At(10, 10).IsEmpty);
        Assert.True(sim.Spatial.At(-1, 3).IsEmpty);
        Assert.True(sim.Spatial.At(3, 99).IsEmpty);
    }

    [Fact]
    public void The_maw_is_every_players_enemy_and_its_own_ally()
    {
        var sim = Create();
        var maw = GameSimulation.MawPlayerId;

        Assert.True(sim.AreEnemies(maw, 0));
        Assert.True(sim.AreEnemies(1, maw));
        Assert.False(sim.AreEnemies(maw, maw));
        Assert.False(sim.AreEnemies(maw, -1));
        Assert.False(sim.AreEnemies(maw, 2));
        Assert.True(sim.AreEnemies(0, 1));
        Assert.False(sim.AreEnemies(0, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Indexed_target_search_matches_a_scan_of_every_entity(int seed)
    {
        // Random crowds of units, Maw-owned bodies and structures; every scan point must pick the same target as the
        // original linear scan, including its quirk that a structure met first can hide a farther unit.
        var random = new DeterministicRandom((ulong)seed);
        var sim = new GameSimulation(7, new GridMap(40, 40), TestMatch.Content,
            [new PlayerSetup(0, BulwarkContent.FactionId), new PlayerSetup(1, BulwarkContent.FactionId), new PlayerSetup(0, BulwarkContent.FactionId)],
            victoryEnabled: false);
        var owners = new[] { 0, 1, 2, GameSimulation.MawPlayerId, -1 };
        for (var i = 0; i < 12; i++)
        {
            var origin = new Cell(random.Next(0, 38), random.Next(0, 38));
            var footprintFree = true;
            for (var dy = 0; dy < 2 && footprintFree; dy++)
            {
                for (var dx = 0; dx < 2; dx++)
                {
                    footprintFree &= sim.Map.IsBuildable(origin + new Cell(dx, dy));
                }
            }

            if (footprintFree)
            {
                sim.PlaceStructure(random.Next(3), Structure(BulwarkContent.PowerPlant), origin);
            }
        }

        for (var i = 0; i < 300; i++)
        {
            var position = new FixVec2(random.NextFix64(Fix64.Zero, Fix64.FromInt(40)), random.NextFix64(Fix64.Zero, Fix64.FromInt(40)));
            sim.SpawnUnit(owners[random.Next(owners.Length)], Unit(BulwarkContent.Rifleman), position);
        }

        // Kill some, then spawn more into the freed slots, so store order is no longer creation or index order.
        for (var i = 0; i < 40; i++)
        {
            sim.World.Destroy(sim.World.EntityAt(random.Next(sim.World.Capacity)));
        }

        sim.World.FlushDestroyed();
        for (var i = 0; i < 60; i++)
        {
            var position = new FixVec2(random.NextFix64(Fix64.Zero, Fix64.FromInt(40)), random.NextFix64(Fix64.Zero, Fix64.FromInt(40)));
            sim.SpawnUnit(owners[random.Next(owners.Length)], Unit(BulwarkContent.Rifleman), position);
        }

        sim.Spatial.Rebuild(sim.World);
        var context = new TickContext(0, sim);
        var candidates = new List<TargetingSystem.Candidate>();
        for (var probe = 0; probe < 400; probe++)
        {
            var position = new FixVec2(random.NextFix64(Fix64.Zero, Fix64.FromInt(40)), random.NextFix64(Fix64.Zero, Fix64.FromInt(40)));
            var owner = owners[random.Next(4)];
            var range = Fix64.FromInt(random.Next(1, 9));

            Assert.Equal(
                ScanEveryEntity(sim, owner, position, range),
                TargetingSystem.FindNearestEnemy(context, candidates, owner, position, range));
        }
    }

    // The pre-grid implementation, kept as the reference.
    private static Entity ScanEveryEntity(GameSimulation sim, int owner, FixVec2 position, Fix64 range)
    {
        var world = sim.World;
        var indices = world.Store<Health>().EntityIndices;
        var best = Entity.None;
        var bestDistance = range * range;
        var bestIsStructure = true;
        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            if (!sim.AreEnemies(owner, world.Store<Owner>().Get(index).PlayerId))
            {
                continue;
            }

            var candidate = world.EntityAt(index);
            var distance = FixVec2.DistanceSquared(position, sim.ClosestPointOf(candidate, position));
            var isStructure = world.Has<StructureInfo>(candidate);
            if (distance > bestDistance)
            {
                continue;
            }

            var better = best.IsNone
                || (bestIsStructure && !isStructure)
                || (bestIsStructure == isStructure && (distance < bestDistance || (distance == bestDistance && index < best.Index)));
            if (better)
            {
                best = candidate;
                bestDistance = distance;
                bestIsStructure = isStructure;
            }
        }

        return best;
    }
}
