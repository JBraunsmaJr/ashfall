﻿using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Orders;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>
/// Ground a unit cannot walk to: a pocket sealed off by a one-cell diagonal wall (the pathfinder never cuts a blocked
/// corner). Units must not chase what is in there, or they re-plan a route that never arrives, tick after tick.
/// </summary>
public class ReachabilityTests
{
    // A 32x32 map with the corner beyond x + y = 51 cut off by a staircase of rock.
    private static GridMap Pocketed()
    {
        var map = new GridMap(32, 32);
        for (var i = 0; i < 12; i++)
        {
            map.SetTerrain(new Cell(20 + i, 31 - i), Terrain.Rock);
        }

        return map;
    }

    [Fact]
    public void Regions_tell_reachable_ground_from_sealed_pockets_and_follow_new_structures()
    {
        var sim = Create(map: Pocketed());
        var navigator = sim.Navigator;
        Assert.True(navigator.CanReach(new Cell(2, 2), new Cell(25, 20)));
        Assert.False(navigator.CanReach(new Cell(2, 2), new Cell(28, 28)));
        Assert.True(navigator.CanReach(new Cell(28, 28), new Cell(30, 30)));
        Assert.False(navigator.CanReach(new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2)), new Cell(27, 29).Center));

        // A wall line across the open map splits it, and the regions follow.
        Assert.True(navigator.CanReach(new Cell(2, 10), new Cell(2, 14)));
        for (var x = 0; x < 32; x++)
        {
            if (sim.Map.IsPassable(new Cell(x, 12)))
            {
                sim.PlaceStructure(0, Structure(BulwarkContent.Wall), new Cell(x, 12));
            }
        }

        Assert.False(navigator.CanReach(new Cell(2, 10), new Cell(2, 14)));
    }

    [Fact]
    public void A_unit_ignores_an_enemy_it_can_neither_shoot_nor_reach()
    {
        var sim = Create(map: Pocketed());
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new FixVec2(Fix64.FromRatio(47, 2), Fix64.FromRatio(51, 2)));
        var trapped = sim.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new FixVec2(Fix64.FromRatio(55, 2), Fix64.FromRatio(57, 2)));
        sim.World.Get<Armament>(trapped).Stance = UnitStance.HoldGround;
        var start = PositionOf(sim, rifleman);
        Run(sim, 90);
        Assert.True(sim.World.Get<Armament>(rifleman).Target.IsNone);
        Assert.Equal(start, PositionOf(sim, rifleman));
        Assert.Equal(OrderKind.Idle, sim.World.Get<UnitOrder>(rifleman).Kind);

        // The same enemy on open ground is chased.
        var open = Create();
        var hunter = open.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new FixVec2(Fix64.FromRatio(47, 2), Fix64.FromRatio(51, 2)));
        var target = open.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new FixVec2(Fix64.FromRatio(55, 2), Fix64.FromRatio(57, 2)));
        open.World.Get<Armament>(target).Stance = UnitStance.HoldGround;
        RunUntil(open, s => s.World.Get<Armament>(hunter).Target == target, 10);
    }

    [Fact]
    public void An_attack_order_on_something_out_of_reach_does_not_replan_every_tick()
    {
        var sim = Create(map: Pocketed());
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(10, 10).Center);
        var trapped = sim.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new FixVec2(Fix64.FromRatio(55, 2), Fix64.FromRatio(57, 2)));
        sim.World.Get<Armament>(trapped).Stance = UnitStance.HoldGround;
        Run(sim, 1, Commands.Command.Attack(0, rifleman, trapped));

        // It walks as close as the ground allows, then waits there, re-planning only now and then.
        RunUntil(sim, s => !s.World.Get<Mover>(rifleman).HasTarget, 400);
        var resting = PositionOf(sim, rifleman);
        Run(sim, 60);
        Assert.True(FixVec2.DistanceSquared(resting, PositionOf(sim, rifleman)) <= Fix64.One);
        Assert.Equal(trapped, sim.World.Get<Armament>(rifleman).Target);
    }
}
