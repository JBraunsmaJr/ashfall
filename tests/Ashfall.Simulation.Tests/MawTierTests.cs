using Ashfall.Content;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using Ashfall.Simulation.Systems;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>The Maw's specialists and behemoths, and the nests that evolve to breed them.</summary>
public class MawTierTests
{
    private static GameContent Rules => TestMatch.Content;

    private static int Creature(string id) => Rules.CreatureIndex(id);

    [Fact]
    public void A_spitter_arcs_acid_over_the_wall_at_the_building_behind_it()
    {
        var sim = MawMatch();
        var plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(10, 4)); // rows 4-5
        for (var x = 0; x < 24; x++)
        {
            sim.PlaceStructure(0, Structure(BulwarkContent.Wall), new Cell(x, 8));
        }

        var spitter = sim.SpawnCreature(Creature(MawContent.Spitter), new Cell(11, 14).Center);
        RunUntil(sim, s => s.World.Get<Health>(plant).Current < 500, maxTicks: 200);

        var acid = DamageTable.Apply(12, Warhead.Acid, ArmorType.Structure);
        Assert.Equal(500 - acid, sim.World.Get<Health>(plant).Current);
        Assert.Equal(plant, sim.World.Get<Creature>(spitter).Target);
        foreach (var index in sim.World.Store<StructureInfo>().EntityIndices)
        {
            if (index != plant.Index)
            {
                Assert.Equal(400, sim.World.Store<Health>().Get(index).Current); // the wall is untouched
            }
        }
    }

    [Fact]
    public void A_siegebeast_breaks_a_wall_in_five_blows()
    {
        var sim = MawMatch();
        sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(10, 2));
        var walls = new List<Ecs.Entity>();
        for (var x = 0; x < 24; x++)
        {
            walls.Add(sim.PlaceStructure(0, Structure(BulwarkContent.Wall), new Cell(x, 8)));
        }

        sim.SpawnCreature(Creature(MawContent.Siegebeast), new Cell(11, 11).Center);
        var firstBlow = RunUntil(sim, s => walls.Any(wall => s.World.Get<Health>(wall).Current < 400), maxTicks: 300);
        var broken = RunUntil(sim, s => walls.Any(wall => !s.World.IsAlive(wall)), maxTicks: 200);

        Assert.InRange(broken, (4 * 30) - 2, (4 * 30) + 2); // four more blows, one every 30 ticks
        Assert.True(firstBlow > 0);
    }

    [Fact]
    public void A_siegebeast_fills_its_cell_so_no_cell_is_ever_overfull()
    {
        var sim = MawMatch();
        sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(11, 2));
        sim.SpawnCreature(Creature(MawContent.Siegebeast), new Cell(12, 12).Center);
        for (var i = 0; i < 40; i++)
        {
            sim.SpawnCreature(Creature(MawContent.Skitter), new Cell(i % 24, 20 + (i / 24)).Center);
        }

        for (var tick = 0; tick < 300; tick++)
        {
            sim.Step([]);
            Assert.InRange(MostWeightInOneCell(sim), 1, SwarmSystem.CellCapacity);
        }
    }

    [Fact]
    public void A_well_fed_pit_evolves_into_a_brood_nest_in_place()
    {
        var sim = MawMatch(population: 0); // feed only
        var pit = sim.PlaceNest(Rules.StructureIndex(MawContent.SpawningPit), new Cell(10, 10));
        LayField(sim.Map, new Cell(11, 11), radius: 5);
        var pitRules = Rules.NestFor(Rules.StructureIndex(MawContent.SpawningPit))!;
        sim.World.Get<Health>(pit).Current = 2_000; // wounded: evolving keeps the wound
        sim.World.Get<Nest>(pit) = sim.World.Get<Nest>(pit) with { Eaten = pitRules.EvolveAfter - pitRules.FeedAmount };

        Run(sim, pitRules.FeedIntervalTicks);

        Assert.Equal(Rules.StructureIndex(MawContent.BroodNest), sim.World.Get<StructureInfo>(pit).StructureType);
        Assert.Equal((3_500, 4_000), (sim.World.Get<Health>(pit).Current, sim.World.Get<Health>(pit).Max));
        Assert.Equal(0, sim.World.Get<Nest>(pit).Eaten);
        Assert.Equal(new Cell(10, 10), sim.World.Get<StructureInfo>(pit).Origin);
    }

    [Fact]
    public void A_starving_pit_never_evolves()
    {
        var sim = MawMatch(population: 1_000);
        var pit = sim.PlaceNest(Rules.StructureIndex(MawContent.SpawningPit), new Cell(10, 10));
        Run(sim, 3_000);

        Assert.Equal(Rules.StructureIndex(MawContent.SpawningPit), sim.World.Get<StructureInfo>(pit).StructureType);
        Assert.Equal(0, sim.World.Get<Nest>(pit).Eaten);
    }

    [Fact]
    public void A_brood_nest_breeds_about_one_spitter_in_seven()
    {
        var sim = MawMatch(population: 5_000, size: 64);
        var brood = sim.PlaceNest(Rules.StructureIndex(MawContent.BroodNest), new Cell(30, 30));
        LayField(sim.Map, new Cell(31, 31), radius: 7);

        Run(sim, 3_000);

        var spitters = 0;
        var skitters = 0;
        foreach (var creature in sim.World.Store<Creature>().Components)
        {
            spitters += creature.CreatureType == Creature(MawContent.Spitter) ? 1 : 0;
            skitters += creature.CreatureType == Creature(MawContent.Skitter) ? 1 : 0;
        }

        Assert.True(sim.World.IsAlive(brood));
        Assert.True(spitters + skitters > 100, $"Only {spitters + skitters} spawned.");
        Assert.InRange(spitters * 7, (spitters + skitters) / 2, (spitters + skitters) * 2);
    }

    private static GameSimulation MawMatch(int population = 0, int size = 24) =>
        new(7, new GridMap(size, size), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false,
            horde: new HordeSettings { CreatureType = 0, Population = population, EdgeSpawns = false });

    private static void LayField(GridMap map, Cell center, int radius)
    {
        for (var y = center.Y - radius; y <= center.Y + radius; y++)
        {
            for (var x = center.X - radius; x <= center.X + radius; x++)
            {
                var cell = new Cell(x, y);
                if (map.InBounds(cell) && map.StructureAt(cell) < 0)
                {
                    map.SetCindrite(cell, 400);
                }
            }
        }
    }

    private static int MostWeightInOneCell(GameSimulation sim)
    {
        var weight = new Dictionary<Cell, int>();
        var creatures = sim.World.Store<Creature>();
        foreach (var index in creatures.EntityIndices)
        {
            var cell = Cell.Containing(sim.World.Store<Position>().Get(index).Value);
            weight[cell] = weight.GetValueOrDefault(cell) + Rules.Creature(creatures.Get(index).CreatureType).CellWeight;
        }

        return weight.Count == 0 ? 1 : weight.Values.Max();
    }
}
