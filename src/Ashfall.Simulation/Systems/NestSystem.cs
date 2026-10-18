﻿using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Keeps every Maw nest alive (GDD section 3): a nest feeds on the Cindrite within its feed radius, turning it into
/// biomass, and spends biomass on creatures that emerge at its edge. A nest whose fields are harvested out or
/// purged stops gaining biomass and, once its store is spent, stops spawning: it starves.
/// </summary>
/// <remarks>
/// Each feed takes from the richest cell in reach (lowest cell index on ties), so a nest grazes its field down
/// evenly instead of boring one hole. Nests together never raise the Maw past
/// <see cref="Horde.HordeSettings.Population"/> creatures alive.
/// </remarks>
public sealed class NestSystem : ISystem
{
    public void Update(in TickContext context)
    {
        var world = context.World;
        var nests = world.Store<Nest>();
        var maw = context.Simulation.Maw;
        if (nests.Count == 0 || maw is null)
        {
            return;
        }

        var structures = world.Store<StructureInfo>();
        var creatures = world.Store<Creature>();
        var indices = nests.EntityIndices;
        var states = nests.Components;
        for (var i = 0; i < states.Length; i++)
        {
            var index = indices[i];
            ref var nest = ref states[i];
            var info = structures.Get(index);
            var structure = context.Content.Structure(info.StructureType);
            var rules = context.Content.NestFor(info.StructureType)!;
            var center = new FixVec2(
                Fix64.FromInt(info.Origin.X) + Fix64.FromRatio(structure.Width, 2),
                Fix64.FromInt(info.Origin.Y) + Fix64.FromRatio(structure.Height, 2));

            if (context.IsStaggeredTick(index, rules.FeedIntervalTicks) && nest.Biomass < rules.MaxBiomass)
            {
                var wanted = rules.MaxBiomass - nest.Biomass < rules.FeedAmount ? rules.MaxBiomass - nest.Biomass : rules.FeedAmount;
                nest.Biomass += Feed(context.Map, center, rules.FeedRadius, wanted);
            }

            if (nest.SpawnTimer > 0)
            {
                nest.SpawnTimer--;
            }

            if (nest.SpawnTimer > 0)
            {
                continue; // spawns are SpawnIntervalTicks apart
            }

            var creatureType = context.Content.CreatureIndex(rules.CreatureId);
            var cost = context.Content.Creature(creatureType).BiomassCost;
            if (nest.Biomass < cost || creatures.Count >= maw.Settings.Population
                || !context.Navigator.TryFindNearestPassable(context.Simulation.ExitCell(info), maxRadius: 4, out var exit))
            {
                continue; // starving, at the population limit, or walled in: try again next tick
            }

            var jitter = Fix64.FromRatio(2, 5);
            var position = exit.Center + new FixVec2(maw.Random.NextFix64(-jitter, jitter), maw.Random.NextFix64(-jitter, jitter));
            context.Simulation.SpawnCreature(creatureType, position);
            nest.Biomass -= cost;
            nest.SpawnTimer = rules.SpawnIntervalTicks;
            maw.Spawned++;
            if (creatures.Count > maw.PeakAlive)
            {
                maw.PeakAlive = creatures.Count;
            }
        }
    }

    // Takes up to `wanted` Cindrite from the cells whose centres lie within `radius` of `center`, richest cell first.
    private static int Feed(GridMap map, FixVec2 center, int radius, int wanted)
    {
        var taken = 0;
        var reach = Fix64.FromInt(radius);
        var (minX, minY) = (center.X.FloorToInt() - radius, center.Y.FloorToInt() - radius);
        var (maxX, maxY) = (center.X.FloorToInt() + radius, center.Y.FloorToInt() + radius);
        while (taken < wanted)
        {
            var best = new Cell(-1, -1);
            var bestAmount = 0;
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var cell = new Cell(x, y);
                    var amount = map.CindriteAt(cell);
                    if (amount > bestAmount && FixVec2.DistanceSquared(cell.Center, center) <= reach * reach)
                    {
                        best = cell;
                        bestAmount = amount;
                    }
                }
            }

            if (best.X < 0)
            {
                break; // nothing left in reach
            }

            taken += map.TakeCindrite(best, wanted - taken);
        }

        return taken;
    }
}
