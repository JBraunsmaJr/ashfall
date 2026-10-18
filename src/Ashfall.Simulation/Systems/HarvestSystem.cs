using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Orders;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Runs every harvester's loop: find Cindrite, drive there, harvest until full or the cell is empty,
/// drive to the nearest refinery, unload into the owner's credits, repeat. A harvester that has been
/// given a Move order pauses the loop until it arrives.
/// </summary>
public sealed class HarvestSystem : ISystem
{
    // How far from the last field cell to look first, so a harvester keeps working the same field.
    private const int LocalSearchRadius = 4;

    // Squared distance (in cells) from the dock at which unloading may begin.
    private static readonly Fix64 DockReachSquared = Fix64.FromRatio(9, 4);

    public void Update(in TickContext context)
    {
        var harvesters = context.World.Store<Harvester>();
        var indices = harvesters.EntityIndices;
        var states = harvesters.Components;
        for (var i = 0; i < states.Length; i++)
        {
            var unit = context.World.EntityAt(indices[i]);
            ref var order = ref context.World.Get<UnitOrder>(unit);
            ref var mover = ref context.World.Get<Mover>(unit);
            if (order.Kind == OrderKind.Move)
            {
                if (mover.HasTarget)
                {
                    continue;
                }

                order = UnitOrder.Idle;
            }

            Run(context, unit, ref states[i], ref mover);
        }
    }

    private static void Run(in TickContext context, Entity unit, ref Harvester harvester, ref Mover mover)
    {
        var world = context.World;
        var position = world.Get<Position>(unit).Value;
        var capacity = context.Content.Unit(world.Get<UnitInfo>(unit).UnitType).HarvestCapacity;
        var economy = context.Content.Economy;

        switch (harvester.State)
        {
            case HarvestState.Idle:
                if (harvester.Cargo >= capacity)
                {
                    StartReturn(context, unit, ref harvester, ref mover, position);
                }
                else if (context.IsStaggeredTick(unit.Index, 8) && TryFindField(context, harvester, position, out var field))
                {
                    harvester.Field = field;
                    harvester.State = HarvestState.MovingToField;
                    context.Navigator.PlanRoute(unit.Index, position, field.Center, ref mover);
                }
                else if (harvester.Cargo > 0 && context.Map.TotalCindrite == 0)
                {
                    StartReturn(context, unit, ref harvester, ref mover, position);
                }

                break;

            case HarvestState.MovingToField:
                if (mover.HasTarget)
                {
                    break;
                }

                if (Cell.Containing(position) == harvester.Field && context.Map.CindriteAt(harvester.Field) > 0)
                {
                    harvester.State = HarvestState.Harvesting;
                    harvester.Timer = economy.HarvestIntervalTicks;
                }
                else
                {
                    harvester.State = HarvestState.Idle;
                }

                break;

            case HarvestState.Harvesting:
                if (--harvester.Timer > 0)
                {
                    break;
                }

                var wanted = Min(economy.HarvestAmount, capacity - harvester.Cargo);
                harvester.Cargo += context.Map.TakeCindrite(harvester.Field, wanted);
                harvester.Timer = economy.HarvestIntervalTicks;
                if (harvester.Cargo >= capacity)
                {
                    StartReturn(context, unit, ref harvester, ref mover, position);
                }
                else if (context.Map.CindriteAt(harvester.Field) == 0)
                {
                    harvester.State = HarvestState.Idle;
                }

                break;

            case HarvestState.MovingToRefinery:
                if (!world.IsAlive(harvester.Refinery))
                {
                    harvester.State = HarvestState.Idle;
                    StartReturn(context, unit, ref harvester, ref mover, position);
                    break;
                }

                if (mover.HasTarget)
                {
                    break;
                }

                var dock = context.Simulation.ExitCell(world.Get<StructureInfo>(harvester.Refinery)).Center;
                if (FixVec2.DistanceSquared(position, dock) <= DockReachSquared)
                {
                    harvester.State = HarvestState.Unloading;
                }
                else if (context.IsStaggeredTick(unit.Index, 15))
                {
                    context.Navigator.PlanRoute(unit.Index, position, dock, ref mover);
                }

                break;

            case HarvestState.Unloading:
                var owner = world.Get<Owner>(unit).PlayerId;
                var amount = Min(economy.UnloadPerTick, harvester.Cargo);
                harvester.Cargo -= amount;
                if (owner >= 0 && owner < context.Players.Count)
                {
                    context.Players[owner].Earn(amount);
                }

                if (harvester.Cargo == 0)
                {
                    harvester.State = HarvestState.Idle;
                }

                break;
        }
    }

    private static void StartReturn(in TickContext context, Entity unit, ref Harvester harvester, ref Mover mover, FixVec2 position)
    {
        var refinery = FindNearestRefinery(context, context.World.Get<Owner>(unit).PlayerId, position);
        if (refinery.IsNone)
        {
            // Nowhere to unload: wait (idle with cargo) until a refinery exists.
            harvester.State = HarvestState.Idle;
            mover.HasTarget = false;
            return;
        }

        harvester.Refinery = refinery;
        harvester.State = HarvestState.MovingToRefinery;
        var dock = context.Simulation.ExitCell(context.World.Get<StructureInfo>(refinery)).Center;
        context.Navigator.PlanRoute(unit.Index, position, dock, ref mover);
    }

    private static Entity FindNearestRefinery(in TickContext context, int playerId, FixVec2 position)
    {
        var structures = context.World.Store<StructureInfo>();
        var owners = context.World.Store<Owner>();
        var positions = context.World.Store<Position>();
        var indices = structures.EntityIndices;
        var infos = structures.Components;
        var best = Entity.None;
        var bestDistance = Fix64.MaxValue;
        for (var i = 0; i < infos.Length; i++)
        {
            if (!infos[i].IsComplete
                || owners.Get(indices[i]).PlayerId != playerId
                || context.Content.Structure(infos[i].StructureType).Role != StructureRole.Refinery)
            {
                continue;
            }

            var distance = FixVec2.DistanceSquared(position, positions.Get(indices[i]).Value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = context.World.EntityAt(indices[i]);
            }
        }

        return best;
    }

    private static bool TryFindField(in TickContext context, in Harvester harvester, FixVec2 position, out Cell field)
    {
        var map = context.Map;
        if (map.TotalCindrite == 0)
        {
            field = default;
            return false;
        }

        if (map.InBounds(harvester.Field) && TryFindCindrite(map, harvester.Field, LocalSearchRadius, out field))
        {
            return true;
        }

        var maxRadius = map.Width > map.Height ? map.Width : map.Height;
        return TryFindCindrite(map, Cell.Containing(position), maxRadius, out field);
    }

    // Ring search outward; within the first ring that has any, the closest cell wins (scan order breaks ties).
    private static bool TryFindCindrite(GridMap map, Cell around, int maxRadius, out Cell found)
    {
        for (var radius = 0; radius <= maxRadius; radius++)
        {
            var bestDistance = int.MaxValue;
            found = default;
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (dx != -radius && dx != radius && dy != -radius && dy != radius)
                    {
                        continue;
                    }

                    var cell = new Cell(around.X + dx, around.Y + dy);
                    var distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance && map.CindriteAt(cell) > 0 && map.IsPassable(cell))
                    {
                        bestDistance = distance;
                        found = cell;
                    }
                }
            }

            if (bestDistance != int.MaxValue)
            {
                return true;
            }
        }

        found = default;
        return false;
    }

    private static int Min(int a, int b) => a < b ? a : b;
}
