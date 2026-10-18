using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>The Bulwark's answers to the swarm: the Pyro's area flame and the Wall.</summary>
public class DefenceTests
{
    private static readonly int Wall = Structure(BulwarkContent.Wall);
    private static readonly int Barracks = Structure(BulwarkContent.Barracks);

    private static int Skitter => TestMatch.Content.CreatureIndex(MawContent.Skitter);

    [Fact]
    public void Flame_scorches_everything_around_its_target_but_never_friends_or_buildings()
    {
        var sim = MawMatch();
        sim.SpawnUnit(0, Unit(BulwarkContent.Pyro), new Cell(10, 10).Center);
        var target = sim.SpawnCreature(Skitter, new Cell(12, 10).Center);
        var beside = sim.SpawnCreature(Skitter, new FixVec2(Fix64.FromRatio(25, 2), Fix64.FromRatio(23, 2))); // 0.5 cell off
        var away = sim.SpawnCreature(Skitter, new Cell(12, 14).Center);
        var friend = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new FixVec2(Fix64.FromInt(12), Fix64.FromRatio(21, 2)));
        var plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(13, 9));
        Freeze(sim);

        RunUntil(sim, s => s.World.Get<Health>(target).Current < 40, maxTicks: 30);

        var burn = DamageTable.Apply(10, Warhead.Flame, ArmorType.Biomass);
        Assert.Equal(40 - burn, sim.World.Get<Health>(target).Current);
        Assert.Equal(40 - burn, sim.World.Get<Health>(beside).Current);
        Assert.Equal(40, sim.World.Get<Health>(away).Current);
        Assert.Equal(125, sim.World.Get<Health>(friend).Current);
        Assert.Equal(500, sim.World.Get<Health>(plant).Current);
    }

    [Fact]
    public void A_pyro_burns_down_a_clump_of_skitters_that_overruns_a_rifleman()
    {
        static (long Kills, bool Survived) Fight(string unitId)
        {
            var sim = MawMatch();
            var defender = sim.SpawnUnit(0, Unit(unitId), new Cell(10, 10).Center);
            sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(4, 9));
            for (var i = 0; i < 6; i++)
            {
                sim.SpawnCreature(Skitter, new Cell(15 + (i % 2), 9 + (i / 2)).Center);
            }

            Run(sim, 300);
            return (sim.Maw!.Killed, sim.World.IsAlive(defender));
        }

        Assert.Equal((6L, true), Fight(BulwarkContent.Pyro));
        var rifleman = Fight(BulwarkContent.Rifleman);
        Assert.False(rifleman.Survived);
        Assert.InRange(rifleman.Kills, 0, 2);
    }

    [Fact]
    public void Walls_need_a_barracks_and_do_not_stretch_the_base()
    {
        var sim = WithYard();
        Assert.Equal(PlacementResult.MissingPrerequisite, sim.CheckPlacement(0, Wall, new Cell(7, 3)));

        sim.PlaceStructure(0, Barracks, new Cell(6, 2));
        Assert.Equal(PlacementResult.Ok, sim.CheckPlacement(0, Wall, new Cell(14, 3)));

        // A wall at the edge of the build radius does not let the next one go further out.
        sim.PlaceStructure(0, Wall, new Cell(14, 3));
        Assert.Equal(PlacementResult.TooFarFromBase, sim.CheckPlacement(0, Wall, new Cell(16, 3)));
    }

    [Fact]
    public void Walls_build_alongside_other_construction_and_never_take_its_slot()
    {
        var sim = WithYard();
        sim.PlaceStructure(0, Barracks, new Cell(6, 2));
        var plant = Structure(BulwarkContent.PowerPlant);

        sim.Step([
            Commands.Command.Build(0, plant, new Cell(9, 2)),
            Commands.Command.Build(0, Wall, new Cell(2, 7)),
            Commands.Command.Build(0, Wall, new Cell(3, 7)),
            Commands.Command.Build(0, Wall, new Cell(4, 7)),
        ]);

        var site = sim.Players[0].ConstructionSite;
        Assert.Equal(plant, sim.World.Get<StructureInfo>(site).StructureType);
        Assert.Equal(PlacementResult.AlreadyBuilding, sim.CheckPlacement(0, plant, new Cell(9, 6)));
        Assert.Equal(PlacementResult.Ok, sim.CheckPlacement(0, Wall, new Cell(5, 7)));
        Assert.Equal(5_000 - 300 - (3 * 50), sim.Players[0].Credits);
        Assert.Equal(0, sim.RejectedCommands);
    }

    [Fact]
    public void A_player_left_with_only_walls_is_defeated()
    {
        var sim = Create(victory: true);
        sim.PlaceStructure(0, Structure(BulwarkContent.ConstructionYard), new Cell(2, 2));
        sim.PlaceStructure(1, Wall, new Cell(20, 20));
        sim.PlaceStructure(1, Wall, new Cell(21, 20));

        sim.Step([]);

        Assert.True(sim.Players[1].Defeated);
        Assert.True(sim.IsOver);
        Assert.Equal(0, sim.WinningTeam);
    }

    [Fact]
    public void The_swarm_has_to_chew_through_a_wall_to_reach_what_it_guards()
    {
        // A plant behind a solid wall line across the whole map: the field leads to the wall, and the swarm attacks it.
        var sim = MawMatch();
        var plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(10, 4));
        var walls = new List<Ecs.Entity>();
        for (var x = 0; x < 24; x++)
        {
            walls.Add(sim.PlaceStructure(0, Wall, new Cell(x, 8)));
        }

        for (var i = 0; i < 12; i++)
        {
            sim.SpawnCreature(Skitter, new Cell(4 + i, 20).Center);
        }

        Run(sim, 150); // there in about 75 ticks, then gnawing; a wall holds far longer than this

        Assert.Equal(500, sim.World.Get<Health>(plant).Current);
        Assert.Contains(walls, wall => sim.World.Get<Health>(wall).Current < 400);
        foreach (var index in sim.World.Store<Creature>().EntityIndices)
        {
            Assert.True(sim.World.Store<Position>().Get(index).Value.Y >= Fix64.FromInt(9), "No creature may be past the wall.");
        }
    }

    private static GameSimulation MawMatch() =>
        new(7, new GridMap(24, 24), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false,
            horde: new HordeSettings { CreatureType = Skitter, Population = 0, EdgeSpawns = false });

    // Stops every creature attacking, so a test can look at one exchange of fire.
    private static void Freeze(GameSimulation sim)
    {
        foreach (var index in sim.World.Store<Creature>().EntityIndices)
        {
            sim.World.Store<Creature>().Get(index).Cooldown = int.MaxValue;
        }
    }
}
