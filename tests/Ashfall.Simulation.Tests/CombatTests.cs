using Ashfall.Content;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Orders;
using Ashfall.Simulation.Players;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

public class CombatTests
{
    [Fact]
    public void Idle_units_open_fire_on_enemies_in_range()
    {
        var sim = Create();
        var warden = sim.SpawnUnit(0, Unit(BulwarkContent.Warden), new Cell(5, 5).Center);
        var harvester = sim.SpawnUnit(1, Unit(BulwarkContent.Harvester), new Cell(9, 5).Center);

        RunUntil(sim, s => !s.World.IsAlive(harvester), maxTicks: 1_000);

        Assert.Equal(450, sim.World.Get<Health>(warden).Current);
        Assert.Equal(new Cell(5, 5).Center, PositionOf(sim, warden)); // already in range: no need to move
    }

    [Fact]
    public void Each_shot_deals_table_damage_then_waits_for_the_cooldown()
    {
        var sim = Create();
        sim.SpawnUnit(0, Unit(BulwarkContent.Rocketeer), new Cell(5, 5).Center);
        var harvester = sim.SpawnUnit(1, Unit(BulwarkContent.Harvester), new Cell(8, 5).Center);

        RunUntil(sim, s => s.World.Get<Health>(harvester).Current < 700, maxTicks: 100);
        Assert.Equal(700 - 45, sim.World.Get<Health>(harvester).Current);

        Run(sim, 44);
        Assert.Equal(700 - 45, sim.World.Get<Health>(harvester).Current);
        sim.Step([]);
        Assert.Equal(700 - 90, sim.World.Get<Health>(harvester).Current);
    }

    [Fact]
    public void Teammates_never_shoot_each_other()
    {
        var sim = new GameSimulation(1, new GridMap(32, 32), GameContent.Standard,
            [new PlayerSetup(0, BulwarkContent.FactionId), new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false);
        var a = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(5, 5).Center);
        var b = sim.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new Cell(7, 5).Center);

        Run(sim, 300);

        Assert.Equal(125, sim.World.Get<Health>(a).Current);
        Assert.Equal(125, sim.World.Get<Health>(b).Current);
    }

    [Fact]
    public void An_attack_order_chases_a_target_beyond_acquisition_range()
    {
        var sim = Create();
        var warden = sim.SpawnUnit(0, Unit(BulwarkContent.Warden), new Cell(2, 2).Center);
        var harvester = sim.SpawnUnit(1, Unit(BulwarkContent.Harvester), new Cell(25, 25).Center);

        sim.Step([Command.Attack(0, warden, harvester)]);
        RunUntil(sim, s => !s.World.IsAlive(harvester), maxTicks: 3_000);
        sim.Step([]);

        Assert.Equal(OrderKind.Idle, sim.World.Get<UnitOrder>(warden).Kind);
    }

    [Fact]
    public void Attack_move_fights_through_enemies_and_then_carries_on()
    {
        var sim = Create();
        var warden = sim.SpawnUnit(0, Unit(BulwarkContent.Warden), new Cell(2, 15).Center);
        var harvester = sim.SpawnUnit(1, Unit(BulwarkContent.Harvester), new Cell(14, 15).Center);
        var destination = new Cell(28, 15).Center;

        sim.Step([Command.AttackMove(0, warden, destination)]);
        RunUntil(sim, s => !s.World.IsAlive(harvester), maxTicks: 2_000);
        RunUntil(sim, s => PositionOf(s, warden) == destination, maxTicks: 2_000);
    }

    [Fact]
    public void Destroyed_structures_free_their_footprint()
    {
        var sim = WithYard();
        var plant = sim.PlaceStructure(1, Structure(BulwarkContent.PowerPlant), new Cell(20, 20));

        sim.World.Get<Health>(plant) = sim.World.Get<Health>(plant) with { Current = 0 };
        sim.Step([]);

        Assert.False(sim.World.IsAlive(plant));
        Assert.True(sim.Map.IsPassable(new Cell(21, 21)));
    }

    [Fact]
    public void A_player_with_no_structures_left_is_defeated_and_the_other_team_wins()
    {
        var sim = WithYard(victory: true);
        var plant = sim.PlaceStructure(1, Structure(BulwarkContent.PowerPlant), new Cell(20, 20));
        sim.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new Cell(25, 25).Center);

        Run(sim, 10);
        Assert.False(sim.IsOver);

        sim.World.Get<Health>(plant) = sim.World.Get<Health>(plant) with { Current = 0 };
        sim.Step([]);

        Assert.True(sim.Players[1].Defeated); // units alone do not keep a player in the game
        Assert.False(sim.Players[0].Defeated);
        Assert.True(sim.IsOver);
        Assert.Equal(0, sim.WinningTeam);
    }

    [Fact]
    public void Defeated_players_cannot_build_or_train()
    {
        var sim = WithYard(victory: true);
        sim.PlaceStructure(1, Structure(BulwarkContent.ConstructionYard), new Cell(20, 20));
        var plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(6, 2));
        var barracks = sim.PlaceStructure(0, Structure(BulwarkContent.Barracks), new Cell(2, 7));
        foreach (var structure in new[] { plant, barracks, sim.World.EntityAt(0) })
        {
            sim.World.Get<Health>(structure) = sim.World.Get<Health>(structure) with { Current = 0 };
        }

        sim.Step([]);
        sim.Step([Command.Build(0, Structure(BulwarkContent.PowerPlant), new Cell(6, 2))]);

        Assert.True(sim.Players[0].Defeated);
        Assert.Equal(1, sim.RejectedCommands);
    }

    [Fact]
    public void Without_victory_rules_a_match_never_ends()
    {
        var sim = Create(victory: false);

        Run(sim, 30);

        Assert.False(sim.IsOver);
        Assert.Equal(-1, sim.WinningTeam);
    }
}
