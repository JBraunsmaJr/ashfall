using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

public class EconomyTests
{
    private static readonly int PowerPlant = Structure(BulwarkContent.PowerPlant);
    private static readonly int Refinery = Structure(BulwarkContent.Refinery);
    private static readonly int Barracks = Structure(BulwarkContent.Barracks);
    private static readonly int Factory = Structure(BulwarkContent.Factory);

    [Fact]
    public void Placement_rules_are_checked_in_order()
    {
        var sim = Create();
        Assert.Equal(PlacementResult.NoConstructionYard, sim.CheckPlacement(0, PowerPlant, new Cell(6, 2)));

        sim = WithYard();
        Assert.Equal(PlacementResult.Ok, sim.CheckPlacement(0, PowerPlant, new Cell(6, 2)));
        Assert.Equal(PlacementResult.NotAPlayer, sim.CheckPlacement(5, PowerPlant, new Cell(6, 2)));
        Assert.Equal(PlacementResult.NotBuildable, sim.CheckPlacement(0, Structure(BulwarkContent.ConstructionYard), new Cell(6, 2)));
        Assert.Equal(PlacementResult.MissingPrerequisite, sim.CheckPlacement(0, Refinery, new Cell(6, 2)));
        Assert.Equal(PlacementResult.Blocked, sim.CheckPlacement(0, PowerPlant, new Cell(3, 3)));
        Assert.Equal(PlacementResult.Blocked, sim.CheckPlacement(0, PowerPlant, new Cell(31, 6)));
        Assert.Equal(PlacementResult.TooFarFromBase, sim.CheckPlacement(0, PowerPlant, new Cell(20, 20)));
    }

    [Fact]
    public void Structures_cannot_be_placed_on_cindrite_or_units()
    {
        var map = new GridMap(32, 32);
        map.SetCindrite(new Cell(7, 3), 100);
        var sim = WithYard(map: map);
        sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(6, 8).Center);

        Assert.Equal(PlacementResult.Blocked, sim.CheckPlacement(0, PowerPlant, new Cell(6, 2)));
        Assert.Equal(PlacementResult.Blocked, sim.CheckPlacement(0, PowerPlant, new Cell(6, 7)));
        Assert.Equal(PlacementResult.Ok, sim.CheckPlacement(0, PowerPlant, new Cell(2, 7)));
    }

    [Fact]
    public void Building_costs_credits_up_front_and_completes_after_its_build_time()
    {
        var sim = WithYard();
        var buildTicks = sim.Content.Structure(PowerPlant).BuildTicks;

        Run(sim, buildTicks - 1, Command.Build(0, PowerPlant, new Cell(6, 2)));

        Assert.Equal(5_000 - 300, sim.Players[0].Credits);
        Assert.Equal(0, sim.Players[0].PowerSupply);
        Assert.Equal(PlacementResult.AlreadyBuilding, sim.CheckPlacement(0, Barracks, new Cell(2, 7)));

        sim.Step([]);
        sim.Step([]);

        Assert.Equal(100, sim.Players[0].PowerSupply);
        Assert.Equal(PlacementResult.Ok, sim.CheckPlacement(0, Barracks, new Cell(2, 7)));
    }

    [Fact]
    public void Invalid_builds_are_rejected_and_cost_nothing()
    {
        var sim = WithYard();

        sim.Step([Command.Build(0, Refinery, new Cell(6, 2)), Command.Build(1, PowerPlant, new Cell(6, 2))]);

        Assert.Equal(2, sim.RejectedCommands);
        Assert.Equal(5_000, sim.Players[0].Credits);
        Assert.Equal(5_000, sim.Players[1].Credits);
    }

    [Fact]
    public void Low_power_halves_construction_speed()
    {
        var normal = WithYard();
        normal.PlaceStructure(0, PowerPlant, new Cell(6, 2));
        var starved = WithYard();
        starved.PlaceStructure(0, PowerPlant, new Cell(6, 2));
        starved.PlaceStructure(0, Factory, new Cell(2, 10));
        starved.PlaceStructure(0, Factory, new Cell(6, 10));
        starved.PlaceStructure(0, Factory, new Cell(10, 10)); // demand 120 > supply 100

        var normalTicks = BuildBarracks(normal);
        var starvedTicks = BuildBarracks(starved);

        Assert.True(starved.Players[0].HasLowPower);
        Assert.InRange(starvedTicks, (normalTicks * 2) - 2, (normalTicks * 2) + 2);
    }

    [Fact]
    public void Production_trains_queued_units_in_order()
    {
        var sim = WithYard();
        sim.PlaceStructure(0, PowerPlant, new Cell(6, 2));
        var barracks = sim.PlaceStructure(0, Barracks, new Cell(2, 7));
        var rifleman = Unit(BulwarkContent.Rifleman);
        var rocketeer = Unit(BulwarkContent.Rocketeer);

        Run(sim, 90, Command.Train(0, barracks, rifleman), Command.Train(0, barracks, rocketeer));

        Assert.Equal(5_000 - 150 - 300, sim.Players[0].Credits);
        Assert.Equal(1, CountUnits(sim, 0, BulwarkContent.Rifleman));
        Assert.Equal(0, CountUnits(sim, 0, BulwarkContent.Rocketeer));
        Assert.Equal(1, sim.World.Get<Producer>(barracks).QueueCount);

        Run(sim, 120);

        Assert.Equal(1, CountUnits(sim, 0, BulwarkContent.Rocketeer));
        Assert.True(sim.World.Get<Producer>(barracks).IsIdle);
    }

    [Fact]
    public void Trained_units_appear_outside_the_structure()
    {
        var sim = WithYard();
        sim.PlaceStructure(0, PowerPlant, new Cell(6, 2));
        var barracks = sim.PlaceStructure(0, Barracks, new Cell(2, 7));

        Run(sim, 90, Command.Train(0, barracks, Unit(BulwarkContent.Rifleman)));

        var unit = sim.World.EntityAt(sim.World.Store<UnitInfo>().EntityIndices[0]);
        Assert.True(sim.Map.IsPassable(Cell.Containing(PositionOf(sim, unit))));
    }

    [Fact]
    public void Training_needs_the_right_structure_queue_space_and_credits()
    {
        var sim = WithYard();
        sim.PlaceStructure(0, PowerPlant, new Cell(6, 2));
        var barracks = sim.PlaceStructure(0, Barracks, new Cell(2, 7));
        var factory = sim.PlaceStructure(0, Factory, new Cell(6, 7));
        var harvester = Unit(BulwarkContent.Harvester);
        var rifleman = Unit(BulwarkContent.Rifleman);

        sim.Step([
            Command.Train(0, barracks, harvester), // wrong structure
            Command.Train(1, factory, harvester), // not your factory
            Command.Train(0, factory, harvester),
            Command.Train(0, factory, harvester),
            Command.Train(0, factory, harvester),
            Command.Train(0, factory, harvester), // 800 credits left: cannot afford
        ]);

        Assert.Equal(3, sim.RejectedCommands);
        Assert.Equal(800, sim.Players[0].Credits);

        sim.Step([.. Enumerable.Repeat(Command.Train(0, barracks, rifleman), 6)]);

        Assert.Equal(4, sim.RejectedCommands); // the queue holds five
        Assert.Equal(5, sim.World.Get<Producer>(barracks).QueueCount);
    }

    [Fact]
    public void A_refinery_comes_with_a_harvester()
    {
        var sim = WithYard();
        sim.PlaceStructure(0, PowerPlant, new Cell(6, 2));

        Run(sim, sim.Content.Structure(Refinery).BuildTicks + 1, Command.Build(0, Refinery, new Cell(2, 7)));

        Assert.Equal(1, CountUnits(sim, 0, BulwarkContent.Harvester));
    }

    [Fact]
    public void Harvesters_turn_cindrite_into_credits_and_nothing_is_created_or_lost()
    {
        var map = new GridMap(32, 32);
        for (var x = 14; x < 18; x++)
        {
            for (var y = 2; y < 6; y++)
            {
                map.SetCindrite(new Cell(x, y), 300);
            }
        }

        var sim = WithYard(map: map);
        sim.PlaceStructure(0, PowerPlant, new Cell(6, 2));
        var refinery = sim.PlaceStructure(0, Refinery, new Cell(9, 2));
        var harvester = sim.SpawnUnitAt(refinery, Unit(BulwarkContent.Harvester));
        var total = map.TotalCindrite + sim.Players[0].Credits;

        var firstLoad = RunUntil(sim, s =>
        {
            Assert.Equal(total, s.Map.TotalCindrite + s.Players[0].Credits + s.World.Get<Harvester>(harvester).Cargo);
            return s.Players[0].Credits >= 5_700;
        }, maxTicks: 2_000);

        Assert.Equal(5_700, sim.Players[0].Credits);
        Assert.InRange(firstLoad, 140, 600); // 28 loads of 25 every 5 ticks, plus two short drives

        RunUntil(sim, s => s.Map.TotalCindrite == 0 && s.World.Get<Harvester>(harvester).Cargo == 0, maxTicks: 20_000);
        Assert.Equal(total, sim.Players[0].Credits);
    }

    private static int BuildBarracks(GameSimulation sim)
    {
        sim.Step([Command.Build(0, Barracks, new Cell(2, 6))]);
        Assert.Equal(0, sim.RejectedCommands);
        return 1 + RunUntil(sim, s => s.HasCompletedRole(0, StructureRole.Barracks), maxTicks: 1_000);
    }
}
