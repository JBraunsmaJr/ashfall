﻿using Ashfall.Content;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

public class NestTests
{
    private static int Pit => TestMatch.Content.StructureIndex(MawContent.SpawningPit);

    private static NestDefinition PitRules => TestMatch.Content.NestFor(Pit)!;

    private static int SkitterCost => TestMatch.Content.Creature(TestMatch.Content.CreatureIndex(MawContent.Skitter)).BiomassCost;

    [Fact]
    public void A_fed_nest_turns_cindrite_into_skitters()
    {
        var sim = NestMatch(population: 500);
        var nest = sim.PlaceNest(Pit, new Cell(20, 20)); // centre (21.5, 21.5)
        LayField(sim.Map, new Cell(21, 21), radius: 4, density: 400);
        var cindrite = sim.Map.TotalCindrite;

        Run(sim, 600); // 40 seconds

        var eaten = cindrite - sim.Map.TotalCindrite;
        var spawned = sim.Maw!.Spawned;
        Assert.Equal(spawned, sim.World.Store<Creature>().Count);
        Assert.Equal(40 * PitRules.FeedAmount, eaten); // fed every second, never short
        Assert.Equal(PitRules.StartingBiomass + eaten - (spawned * SkitterCost), sim.World.Get<Nest>(nest).Biomass);
        Assert.True(spawned > 70, $"A fed pit should keep breeding; it made {spawned}.");
    }

    [Fact]
    public void A_nest_without_cindrite_spends_its_store_then_starves()
    {
        var sim = NestMatch(population: 500);
        var nest = sim.PlaceNest(Pit, new Cell(20, 20));

        Run(sim, 900);

        Assert.Equal(PitRules.StartingBiomass / SkitterCost, sim.Maw!.Spawned);
        Assert.Equal(PitRules.StartingBiomass % SkitterCost, sim.World.Get<Nest>(nest).Biomass);
    }

    [Fact]
    public void Harvesting_the_field_out_starves_the_nest()
    {
        var sim = NestMatch(population: 500);
        sim.PlaceNest(Pit, new Cell(20, 20));
        LayField(sim.Map, new Cell(21, 21), radius: 4, density: 400);
        Run(sim, 300);
        var spawnedWhileFed = sim.Maw!.Spawned;

        StripField(sim.Map, new Cell(21, 21), radius: 4); // the players harvest or burn it clean
        Run(sim, 300); // spends what it had stored
        var afterStore = sim.Maw.Spawned;
        Run(sim, 600);

        Assert.True(afterStore > spawnedWhileFed);
        Assert.Equal(afterStore, sim.Maw.Spawned);
    }

    [Fact]
    public void Nests_only_feed_within_their_radius_and_graze_the_richest_cell_first()
    {
        var sim = NestMatch(population: 0); // no spawning: only feeding
        sim.PlaceNest(Pit, new Cell(20, 20));
        sim.Map.SetCindrite(new Cell(21, 25), 300);
        sim.Map.SetCindrite(new Cell(25, 21), 200);
        sim.Map.SetCindrite(new Cell(21, 21 + PitRules.FeedRadius + 1), 500); // out of reach

        Run(sim, PitRules.FeedIntervalTicks); // one feed

        Assert.Equal(300 - PitRules.FeedAmount, sim.Map.CindriteAt(new Cell(21, 25)));
        Assert.Equal(200, sim.Map.CindriteAt(new Cell(25, 21)));
        Run(sim, 900);
        Assert.Equal(500, sim.Map.CindriteAt(new Cell(21, 21 + PitRules.FeedRadius + 1)));
    }

    [Fact]
    public void Nests_respect_the_population_limit_and_die_like_any_structure()
    {
        var sim = NestMatch(population: 6);
        var nest = sim.PlaceNest(Pit, new Cell(20, 20));
        sim.PlaceNest(Pit, new Cell(5, 5));
        Run(sim, 300);
        Assert.Equal(6, sim.World.Store<Creature>().Count);

        sim.World.Get<Health>(nest).Current = 0;
        sim.Step([]);
        Assert.False(sim.World.IsAlive(nest));
        Assert.Equal(-1, sim.Map.StructureAt(new Cell(21, 21)));
        Assert.Equal(1, sim.World.Store<Nest>().Count);
    }

    [Fact]
    public void Players_can_fight_nests_but_only_anti_armour_hurts_them()
    {
        var sim = NestMatch(population: 0);
        var nest = sim.PlaceNest(Pit, new Cell(20, 20));
        sim.SpawnUnit(0, Unit(BulwarkContent.Rocketeer), new Cell(26, 21).Center);
        sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(21, 25).Center);

        RunUntil(sim, s => s.World.Get<Health>(nest).Current < 2_500, maxTicks: 60);
        Run(sim, 30);

        var rocket = DamageTable.Apply(45, Warhead.AntiArmour, ArmorType.Carapace);
        var rifle = DamageTable.Apply(15, Warhead.SmallArms, ArmorType.Carapace);
        Assert.Equal((45, 1), (rocket, rifle));
        Assert.InRange(2_500 - sim.World.Get<Health>(nest).Current, rocket, rocket + (5 * rifle));
    }

    [Fact]
    public void Only_nest_structures_can_be_placed_as_nests_and_only_with_a_horde()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NestMatch(10).PlaceNest(Structure(BulwarkContent.Barracks), new Cell(4, 4)));
        Assert.Throws<InvalidOperationException>(() => Create().PlaceNest(Pit, new Cell(4, 4)));
    }

    private static GameSimulation NestMatch(int population) =>
        new(7, new GridMap(48, 48), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false,
            horde: new HordeSettings { CreatureType = 0, Population = population, EdgeSpawns = false });

    // A disc of field around a centre, leaving any structure cells bare.
    private static void LayField(GridMap map, Cell center, int radius, int density)
    {
        for (var y = center.Y - radius; y <= center.Y + radius; y++)
        {
            for (var x = center.X - radius; x <= center.X + radius; x++)
            {
                var cell = new Cell(x, y);
                var (dx, dy) = (x - center.X, y - center.Y);
                if ((dx * dx) + (dy * dy) <= radius * radius && map.StructureAt(cell) < 0)
                {
                    map.SetCindrite(cell, density);
                }
            }
        }
    }

    private static void StripField(GridMap map, Cell center, int radius)
    {
        for (var y = center.Y - radius; y <= center.Y + radius; y++)
        {
            for (var x = center.X - radius; x <= center.X + radius; x++)
            {
                map.TakeCindrite(new Cell(x, y), int.MaxValue);
            }
        }
    }
}
