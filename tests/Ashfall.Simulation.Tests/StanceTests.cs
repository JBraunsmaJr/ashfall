using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Orders;
using Ashfall.Simulation.Players;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>Units and creatures answer the enemies around them, within what their stance allows.</summary>
public class StanceTests
{
    private static int Skitter => TestMatch.Content.CreatureIndex(MawContent.Skitter);

    [Fact]
    public void A_unit_whose_move_has_arrived_fights_whatever_comes_near()
    {
        var sim = Create();
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(4, 16).Center);
        Run(sim, 120, Command.Move(0, rifleman, new Cell(10, 16).Center));
        Assert.Equal(OrderKind.Idle, sim.World.Get<UnitOrder>(rifleman).Kind);

        var enemy = sim.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new Cell(14, 16).Center);
        sim.World.Get<Armament>(enemy).Stance = UnitStance.HoldGround; // it stays put and fires back only in reach
        RunUntil(sim, s => s.World.Get<Armament>(rifleman).Target == enemy, 5);
    }

    [Fact]
    public void A_guard_chases_a_short_way_then_walks_back_to_its_post()
    {
        var sim = Create(48);
        var post = new Cell(10, 24).Center;
        var guard = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), post);
        var runner = sim.SpawnUnit(1, Unit(BulwarkContent.Harvester), new Cell(15, 24).Center);
        Run(sim, 1, Command.Move(1, runner, new Cell(46, 24).Center));

        RunUntil(sim, s => s.World.Get<Armament>(guard).Target == runner, 8);
        Assert.True(sim.World.Get<Armament>(guard).Guarding);
        Assert.Equal(post, sim.World.Get<Armament>(guard).GuardPoint);

        // The harvester walks off; the guard gives up past its leash and comes home.
        var definition = TestMatch.Content.Unit(Unit(BulwarkContent.Rifleman));
        var furthest = Fix64.Zero;
        for (var tick = 0; tick < 600; tick++)
        {
            sim.Step([]);
            var distance = FixVec2.DistanceSquared(PositionOf(sim, guard), post);
            furthest = distance > furthest ? distance : furthest;
        }

        var leash = definition.AcquisitionRange + Systems.TargetingSystem.GuardLeash + Fix64.One;
        Assert.True(furthest <= leash * leash, "The guard chased too far.");
        Assert.True(FixVec2.DistanceSquared(PositionOf(sim, guard), post) <= Fix64.FromRatio(1, 4), "The guard did not come back.");
        Assert.False(sim.World.Get<Armament>(guard).Guarding);
        Assert.True(sim.World.IsAlive(runner));
    }

    [Fact]
    public void A_free_unit_pursues_wherever_its_target_goes()
    {
        var sim = Create(48);
        var hunter = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(10, 24).Center);
        var runner = sim.SpawnUnit(1, Unit(BulwarkContent.Harvester), new Cell(15, 24).Center);
        Run(sim, 1, Command.SetStance(0, hunter, UnitStance.Free), Command.Move(1, runner, new Cell(46, 24).Center));
        Assert.Equal(UnitStance.Free, sim.World.Get<Armament>(hunter).Stance);

        Run(sim, 400);
        Assert.True(PositionOf(sim, hunter).X > Fix64.FromInt(30), "A free unit should have kept chasing.");
    }

    [Fact]
    public void A_unit_holding_ground_never_moves_and_fires_only_in_weapon_range()
    {
        var sim = Create();
        var post = new Cell(10, 16).Center;
        var holder = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), post);
        var enemy = sim.SpawnUnit(1, Unit(BulwarkContent.Harvester), new Cell(15, 16).Center); // 5 cells: seen, not in reach
        Run(sim, 30, Command.SetStance(0, holder, UnitStance.HoldGround));
        Assert.Equal(post, PositionOf(sim, holder));
        Assert.True(sim.World.Get<Armament>(holder).Target.IsNone);

        Run(sim, 30, Command.Move(1, enemy, new Cell(13, 16).Center));
        Assert.Equal(enemy, sim.World.Get<Armament>(holder).Target);
        Assert.True(sim.World.Get<Health>(enemy).Current < sim.World.Get<Health>(enemy).Max);
        Assert.Equal(post, PositionOf(sim, holder));
    }

    [Fact]
    public void Stances_are_only_for_armed_units_and_known_values()
    {
        var sim = Create();
        var harvester = sim.SpawnUnit(0, Unit(BulwarkContent.Harvester), new Cell(4, 4).Center);
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(6, 4).Center);
        Run(sim, 1,
            Command.SetStance(0, harvester, UnitStance.Free),
            Command.SetStance(0, rifleman, (UnitStance)9),
            Command.SetStance(1, rifleman, UnitStance.Free));
        Assert.Equal(3, sim.RejectedCommands);
        Assert.Equal(UnitStance.Guard, sim.World.Get<Armament>(rifleman).Stance); // the default
    }

    [Fact]
    public void A_shot_unit_turns_on_its_attacker_at_once()
    {
        var sim = Create();
        var victim = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(10, 16).Center);
        var plant = sim.PlaceStructure(1, Structure(BulwarkContent.PowerPlant), new Cell(10, 12));
        RunUntil(sim, s => s.World.Get<Armament>(victim).Target == plant, 10);

        // A rocketeer hits it: the rifleman drops the building for the rocketeer on the tick after the hit.
        var attacker = sim.SpawnUnit(1, Unit(BulwarkContent.Rocketeer), new Cell(15, 16).Center);
        Run(sim, 1, Command.Attack(1, attacker, victim));
        RunUntil(sim, s => s.World.Get<Health>(victim).Current < s.World.Get<Health>(victim).Max, 60);
        Run(sim, 1);
        Assert.Equal(attacker, sim.World.Get<Armament>(victim).Target);
    }

    [Fact]
    public void A_creature_turns_on_a_unit_that_shoots_it_from_beyond_its_aggro_range()
    {
        var sim = MawMatch();
        var siegebeast = sim.SpawnCreature(TestMatch.Content.CreatureIndex(MawContent.Siegebeast), new Cell(16, 12).Center);
        sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(2, 2));
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(12, 12).Center); // 4 cells: twice its aggro range
        Run(sim, 1, Command.Attack(0, rifleman, siegebeast));

        RunUntil(sim, s => s.World.Get<Creature>(siegebeast).Target == rifleman, 40);
    }

    [Fact]
    public void A_creature_chewing_a_wall_turns_on_a_unit_that_comes_near()
    {
        var sim = MawMatch();
        var wall = sim.PlaceStructure(0, Structure(BulwarkContent.Wall), new Cell(12, 12));
        var skitter = sim.SpawnCreature(Skitter, new Cell(13, 12).Center);
        RunUntil(sim, s => s.World.Get<Creature>(skitter).Target == wall, 20);

        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Harvester), new Cell(15, 12).Center);
        RunUntil(sim, s => s.World.Get<Creature>(skitter).Target == rifleman, 10);
    }

    private static GameSimulation MawMatch() =>
        new(7, new GridMap(24, 24), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false,
            horde: new HordeSettings { CreatureType = Skitter, Population = 0, EdgeSpawns = false });
}
