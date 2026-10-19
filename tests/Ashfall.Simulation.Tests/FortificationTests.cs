using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Orders;
using Ashfall.Simulation.Players;
using Ashfall.Simulation.Systems;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>Guard towers that fire on their own, and bunkers infantry shelter in and fire out of.</summary>
public class FortificationTests
{
    private static int Skitter => TestMatch.Content.CreatureIndex(MawContent.Skitter);

    [Fact]
    public void A_guard_tower_shoots_what_comes_in_range_and_an_unfinished_one_does_not()
    {
        var sim = MawMatch();
        var tower = sim.PlaceStructure(0, Structure(BulwarkContent.GuardTower), new Cell(10, 10));
        var site = sim.PlaceStructure(0, Structure(BulwarkContent.GuardTower), new Cell(3, 3), complete: false);
        Assert.Equal(UnitStance.HoldGround, sim.World.Get<Armament>(tower).Stance);

        var skitter = sim.SpawnCreature(Skitter, new Cell(15, 10).Center); // 5 cells off: in reach, out of its own aggro
        var near = sim.SpawnCreature(Skitter, new Cell(5, 3).Center);
        Freeze(sim);
        RunUntil(sim, s => s.World.Get<Health>(skitter).Current < 40, 20);
        Assert.Equal(40, sim.World.Get<Health>(near).Current);
        Assert.True(sim.World.Get<Armament>(site).Target.IsNone);
    }

    [Fact]
    public void Defences_do_not_extend_where_a_player_may_build()
    {
        var sim = WithYard();
        sim.PlaceStructure(0, Structure(BulwarkContent.Barracks), new Cell(6, 2));
        sim.PlaceStructure(0, Structure(BulwarkContent.GuardTower), new Cell(14, 2));
        Assert.Equal(PlacementResult.TooFarFromBase, sim.CheckPlacement(0, Structure(BulwarkContent.PowerPlant), new Cell(19, 2)));
    }

    [Fact]
    public void Infantry_enter_a_bunker_vanish_from_reach_and_fire_out_further_than_in_the_open()
    {
        var sim = MawMatch();
        var bunker = sim.PlaceStructure(0, Structure(BulwarkContent.Bunker), new Cell(10, 10));
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(4, 14).Center);
        Run(sim, 1, Command.Enter(0, rifleman, bunker));
        RunUntil(sim, s => s.IsSheltered(rifleman), 200);
        Assert.Equal(sim.World.Get<Position>(bunker).Value, PositionOf(sim, rifleman));
        Assert.Equal(1, sim.OccupantsOf(bunker));

        // A skitter 6 cells from the bunker's middle: a rifle reaches 4 in the open, 4 + 1 + 1 from inside.
        var skitter = sim.SpawnCreature(Skitter, sim.World.Get<Position>(bunker).Value + new FixVec2(Fix64.FromInt(6), Fix64.Zero));
        Freeze(sim);
        RunUntil(sim, s => s.World.Get<Health>(skitter).Current < 40, 20);

        // Creatures cannot see it: one beside the bunker goes for the bunker, never the rifleman.
        var biter = sim.SpawnCreature(Skitter, new Cell(9, 12).Center);
        Run(sim, 20);
        Assert.NotEqual(rifleman, sim.World.Get<Creature>(biter).Target);
        Assert.Equal(125, sim.World.Get<Health>(rifleman).Current);

        // No orders inside; unloading puts it back outside, idle.
        Run(sim, 1, Command.Move(0, rifleman, new Cell(2, 2).Center));
        Assert.Equal(1, sim.RejectedCommands);
        Run(sim, 1, Command.Unload(0, bunker));
        Assert.False(sim.IsSheltered(rifleman));
        Assert.Equal(OrderKind.Idle, sim.World.Get<UnitOrder>(rifleman).Kind);
        Assert.Equal(-1, sim.Map.StructureAt(Cell.Containing(PositionOf(sim, rifleman))));
    }

    [Fact]
    public void A_full_bunker_turns_the_fifth_away_and_a_fallen_bunker_lets_everyone_out_alive()
    {
        var sim = Create();
        var bunker = sim.PlaceStructure(0, Structure(BulwarkContent.Bunker), new Cell(10, 10));
        var squad = Enumerable.Range(0, 5).Select(i => sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(6 + i, 16).Center)).ToArray();
        Run(sim, 1, squad.Select(unit => Command.Enter(0, unit, bunker)).ToArray());
        RunUntil(sim, s => squad.All(unit => s.World.Get<UnitOrder>(unit).Kind == OrderKind.Idle), 300);
        Assert.Equal(4, sim.OccupantsOf(bunker));
        Assert.Single(squad, unit => !sim.IsSheltered(unit));

        sim.World.Get<Health>(bunker).Current = 0;
        Run(sim, 2);
        Assert.False(sim.World.IsAlive(bunker));
        Assert.All(squad, unit => Assert.True(sim.World.IsAlive(unit) && !sim.IsSheltered(unit)));
    }

    [Fact]
    public void Only_infantry_enter_and_only_a_finished_bunker_of_your_own()
    {
        var sim = Create();
        var bunker = sim.PlaceStructure(0, Structure(BulwarkContent.Bunker), new Cell(10, 10));
        var theirs = sim.PlaceStructure(1, Structure(BulwarkContent.Bunker), new Cell(20, 10));
        var site = sim.PlaceStructure(0, Structure(BulwarkContent.Bunker), new Cell(4, 20), complete: false);
        var tank = sim.SpawnUnit(0, Unit(BulwarkContent.Warden), new Cell(6, 6).Center);
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(6, 8).Center);
        Run(sim, 1,
            Command.Enter(0, tank, bunker),
            Command.Enter(0, rifleman, theirs),
            Command.Enter(0, rifleman, site),
            Command.Unload(1, bunker));
        Assert.Equal(4, sim.RejectedCommands);
    }

    private static GameSimulation MawMatch() =>
        new(7, new GridMap(24, 24), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false,
            horde: new HordeSettings { CreatureType = Skitter, Population = 0, EdgeSpawns = false });

    private static void Freeze(GameSimulation sim)
    {
        foreach (var index in sim.World.Store<Creature>().EntityIndices)
        {
            sim.World.Store<Creature>().Get(index).Cooldown = int.MaxValue;
        }
    }
}
