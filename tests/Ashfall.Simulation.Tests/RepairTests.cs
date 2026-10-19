using Ashfall.Content;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Systems;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>Repairing structures, mending vehicles at a repair bay, and medics healing infantry.</summary>
public class RepairTests
{
    private static int Interval => TestMatch.Content.Economy.RepairIntervalTicks;

    [Fact]
    public void A_structure_under_repair_mends_for_credits_and_stops_when_whole()
    {
        var sim = Create();
        var plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(4, 4));
        sim.World.Get<Health>(plant).Current = 400; // 100 down of 500
        var credits = sim.Players[0].Credits;

        Run(sim, 1, Command.Repair(0, plant, on: true));
        Assert.True(sim.World.Get<StructureInfo>(plant).Repairing);
        RunUntil(sim, s => !s.World.Get<StructureInfo>(plant).Repairing, 20 * Interval);

        Assert.Equal(500, sim.World.Get<Health>(plant).Current);

        // 10 pulses of 10 hit points: each costs half of what 10/500 of a 300-credit plant costs new, rounded up.
        var pulse = RepairSystem.CostOf(300, 10, 500, TestMatch.Content.Economy);
        Assert.Equal(3, pulse);
        Assert.Equal(credits - (10 * pulse), sim.Players[0].Credits);
    }

    [Fact]
    public void Repair_waits_while_the_player_cannot_pay_and_can_be_switched_off()
    {
        var sim = Create();
        var plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(4, 4));
        sim.World.Get<Health>(plant).Current = 100;
        while (sim.Players[0].Credits > 0)
        {
            sim.Players[0].Spend(sim.Players[0].Credits);
        }

        Run(sim, 3 * Interval, Command.Repair(0, plant, on: true));
        Assert.Equal(100, sim.World.Get<Health>(plant).Current);
        Assert.True(sim.World.Get<StructureInfo>(plant).Repairing);

        Run(sim, 1, Command.Repair(0, plant, on: false));
        Assert.False(sim.World.Get<StructureInfo>(plant).Repairing);
        Run(sim, 1, Command.Repair(1, plant, on: true));
        Assert.Equal(1, sim.RejectedCommands); // not your building
    }

    [Fact]
    public void A_repair_bay_mends_vehicles_beside_it_and_nothing_else()
    {
        var sim = Create();
        var bay = sim.PlaceStructure(0, Structure(BulwarkContent.RepairBay), new Cell(10, 10));
        var exit = sim.ExitCell(sim.World.Get<StructureInfo>(bay));
        var tank = sim.SpawnUnit(0, Unit(BulwarkContent.Warden), exit.Center);
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(exit.X + 1, exit.Y).Center);
        var far = sim.SpawnUnit(0, Unit(BulwarkContent.Warden), new Cell(25, 25).Center);
        foreach (var unit in new[] { tank, rifleman, far })
        {
            sim.World.Get<Health>(unit).Current = 50;
        }

        Run(sim, 4 * Interval);
        Assert.True(sim.World.Get<Health>(tank).Current > 50);
        Assert.Equal(50, sim.World.Get<Health>(rifleman).Current);
        Assert.Equal(50, sim.World.Get<Health>(far).Current);
        Assert.True(sim.Players[0].Credits < TestMatch.Content.Economy.StartingCredits);
    }

    [Fact]
    public void A_medic_heals_the_worst_hurt_infantry_nearby_but_not_itself_vehicles_or_enemies()
    {
        var sim = Create();
        var medic = sim.SpawnUnit(0, Unit(BulwarkContent.Medic), new Cell(10, 10).Center);
        var scratched = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(11, 10).Center);
        var wounded = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(10, 11).Center);
        var tank = sim.SpawnUnit(0, Unit(BulwarkContent.Warden), new Cell(9, 10).Center);
        var enemy = sim.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new Cell(10, 13).Center);
        sim.World.Get<Armament>(enemy).Stance = Orders.UnitStance.HoldGround;
        sim.World.Get<Health>(scratched).Current = 120;
        sim.World.Get<Health>(wounded).Current = 40;
        sim.World.Get<Health>(tank).Current = 100;
        sim.World.Get<Health>(enemy).Current = 40;
        sim.World.Get<Health>(medic).Current = 10;
        foreach (var unit in new[] { scratched, wounded, enemy, tank })
        {
            sim.World.Get<Armament>(unit).Cooldown = int.MaxValue; // nobody shoots: watch the healing alone
        }

        Run(sim, Interval); // one pulse (tick 0)
        Assert.Equal(40 + 12, sim.World.Get<Health>(wounded).Current);
        Assert.Equal(120, sim.World.Get<Health>(scratched).Current);
        Assert.Equal(100, sim.World.Get<Health>(tank).Current);
        Assert.Equal(40, sim.World.Get<Health>(enemy).Current);
        Assert.Equal(10, sim.World.Get<Health>(medic).Current);
        Assert.Equal(TestMatch.Content.Economy.StartingCredits, sim.Players[0].Credits); // healing is free
    }
}
