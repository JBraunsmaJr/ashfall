using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using Ashfall.Simulation.Systems;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

public class SwarmTests
{
    private static int Skitter => TestMatch.Content.CreatureIndex(MawContent.Skitter);

    [Fact]
    public void The_flow_field_measures_the_walk_to_the_nearest_player_structure()
    {
        var map = new GridMap(20, 20);
        for (var y = 0; y < 20; y++)
        {
            if (y != 10)
            {
                map.SetTerrain(new Cell(14, y), Terrain.Rock); // a wall with one gap at y = 10
            }
        }

        var sim = Create(map: map);
        sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(4, 4)); // covers (4..5, 4..5)
        var field = sim.RefreshSwarmField();

        Assert.Equal(4, field.SourceCells);
        Assert.Equal(0, field.CostAt(new Cell(4, 4)));
        Assert.Equal(FlowField.StraightCost, field.CostAt(new Cell(6, 4)));
        Assert.Equal(FlowField.DiagonalCost, field.CostAt(new Cell(6, 6)));
        Assert.Equal(FlowField.StraightCost * 3, field.CostAt(new Cell(8, 5)));
        Assert.Equal(FlowField.Unreachable, field.CostAt(new Cell(14, 3))); // rock

        // Beyond the wall the walk detours through the gap, so it costs more than the straight-line distance.
        Assert.True(field.CostAt(new Cell(16, 4)) > FlowField.StraightCost * 10);
    }

    [Fact]
    public void Only_player_structures_attract_the_swarm_and_the_field_follows_the_layout()
    {
        var sim = Create();
        sim.PlaceStructure(GameSimulation.MawPlayerId, Structure(BulwarkContent.PowerPlant), new Cell(20, 20));
        Assert.Equal(0, sim.RefreshSwarmField().SourceCells);
        Assert.Equal(FlowField.Unreachable, sim.SwarmField.CostAt(new Cell(5, 5)));

        var plant = sim.PlaceStructure(1, Structure(BulwarkContent.PowerPlant), new Cell(4, 4));
        Assert.Equal(4, sim.RefreshSwarmField().SourceCells);
        Assert.Equal(FlowField.StraightCost, sim.SwarmField.CostAt(new Cell(6, 5)));

        sim.World.Get<Health>(plant).Current = 0;
        sim.Step([]);
        Assert.Equal(0, sim.RefreshSwarmField().SourceCells);
    }

    [Fact]
    public void Creatures_walk_to_the_nearest_structure_and_tear_into_it()
    {
        var sim = Create(size: 40);
        var yard = sim.PlaceStructure(0, Structure(BulwarkContent.ConstructionYard), new Cell(18, 18));
        var creature = sim.SpawnCreature(Skitter, new Cell(2, 3).Center);

        var ticks = RunUntil(sim, s => s.World.Get<Health>(yard).Current < 1_500, maxTicks: 300);

        // About 15 cells at 0.16 cells per tick: there in roughly 100 ticks, without any per-creature pathfinding.
        Assert.InRange(ticks, 60, 160);
        Assert.Equal(yard, sim.World.Get<Creature>(creature).Target);
        var claws = TestMatch.Content.Creature(Skitter).Weapon;
        Assert.Equal(1_500 - DamageTable.Apply(claws.Damage, claws.Warhead, ArmorType.Structure), sim.World.Get<Health>(yard).Current);
    }

    [Fact]
    public void Creatures_turn_on_units_they_notice_and_units_shoot_back()
    {
        var sim = new GameSimulation(7, new GridMap(32, 32), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false,
            horde: new HordeSettings { CreatureType = Skitter, Population = 0 });
        sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(28, 28));
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(10, 10).Center);
        var creature = sim.SpawnCreature(Skitter, new Cell(10, 13).Center);

        RunUntil(sim, s => s.World.Get<Health>(rifleman).Current < 125 || !s.World.IsAlive(creature), maxTicks: 60);
        RunUntil(sim, s => !s.World.IsAlive(creature) || !s.World.IsAlive(rifleman), maxTicks: 400);

        Assert.False(sim.World.IsAlive(creature)); // one rifleman beats one skitter
        Assert.True(sim.World.Get<Health>(rifleman).Current < 125);
        Assert.Equal(1, sim.Maw!.Killed);
    }

    [Fact]
    public void No_cell_holds_more_creatures_than_its_capacity_once_they_move()
    {
        var sim = Create(size: 48);
        sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(23, 23));
        for (var i = 0; i < 120; i++)
        {
            // One creature per cell on the map's rim, all converging on one small structure.
            var cell = i < 47 ? new Cell(i, 0) : i < 94 ? new Cell(47, i - 47) : new Cell(i - 94, 47);
            sim.SpawnCreature(Skitter, cell.Center);
        }

        var crowded = 0;
        for (var tick = 0; tick < 400; tick++)
        {
            sim.Step([]);
            crowded = Math.Max(crowded, MostCreaturesInOneCell(sim));
        }

        Assert.InRange(crowded, 2, SwarmSystem.CellCapacity);
    }

    [Fact]
    public void The_spawner_keeps_the_population_up_from_the_edges_on_its_own_random_stream()
    {
        var horde = new HordeSettings { CreatureType = Skitter, Population = 150, SpawnPerTick = 30, EdgeDepth = 2, MinimumDistance = 10 };
        var withHorde = FortressMatch(horde);
        var withoutHorde = FortressMatch(null);

        withHorde.Step([]);
        Assert.Equal(30, withHorde.World.Store<Creature>().Count);
        foreach (var index in withHorde.World.Store<Creature>().EntityIndices)
        {
            var cell = Cell.Containing(withHorde.World.Store<Position>().Get(index).Value);
            Assert.True(cell.X < 2 || cell.Y < 2 || cell.X >= 38 || cell.Y >= 38, $"{cell} is not on the edge.");
        }

        Run(withHorde, 9);
        Run(withoutHorde, 10);
        Assert.Equal(150, withHorde.World.Store<Creature>().Count);
        Assert.Equal(150, withHorde.Maw!.Spawned);
        Assert.Equal(withoutHorde.Random.Snapshot, withHorde.Random.Snapshot); // the match's own stream is untouched
    }

    [Fact]
    public void Two_hordes_from_the_same_seed_play_out_identically()
    {
        var horde = new HordeSettings { CreatureType = Skitter, Population = 300, SpawnPerTick = 25 };
        var a = FortressMatch(horde);
        var b = FortressMatch(horde);
        for (var tick = 0; tick < 600; tick++)
        {
            a.Step([]);
            b.Step([]);
            if (tick % 50 == 0)
            {
                Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
            }
        }

        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        Assert.True(a.Maw!.Killed > 0, "The defenders should have killed something.");
        Assert.True(a.Maw.Spawned > 300, "The spawner should have replaced losses.");

        var other = new GameSimulation(8, new GridMap(40, 40), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false, horde);
        other.Step([]);
        a = FortressMatch(horde);
        a.Step([]);
        Assert.NotEqual(Positions(a), Positions(other));
    }

    [Fact]
    public void Matches_without_creatures_hash_as_if_the_maw_did_not_exist()
    {
        // Registering the Creature store and adding the swarm systems must not change any existing match's hash:
        // the golden replays depend on it. Empty stores and dead slots add nothing; Maw state is hashed only when present.
        var sim = WithYard();
        var withCreature = WithYard();
        var creature = withCreature.SpawnCreature(Skitter, new Cell(30, 30).Center);
        Assert.NotEqual(sim.ComputeStateHash(), withCreature.ComputeStateHash());
        withCreature.World.Destroy(creature);
        withCreature.World.FlushDestroyed();

        Assert.Equal(sim.ComputeStateHash(), withCreature.ComputeStateHash());
        Assert.Null(sim.Maw);
        Run(sim, 30);
        Assert.Equal(0, sim.World.Store<Creature>().Count);
    }

    private static GameSimulation FortressMatch(HordeSettings? horde)
    {
        var sim = new GameSimulation(7, new GridMap(40, 40), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)], victoryEnabled: false, horde);
        sim.PlaceStructure(0, Structure(BulwarkContent.ConstructionYard), new Cell(18, 18));
        for (var i = 0; i < 12; i++)
        {
            sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(15 + (i % 4 * 3), i < 4 ? 15 : i < 8 ? 23 : 19).Center);
        }

        return sim;
    }

    private static int MostCreaturesInOneCell(GameSimulation sim)
    {
        var counts = new Dictionary<Cell, int>();
        foreach (var index in sim.World.Store<Creature>().EntityIndices)
        {
            var cell = Cell.Containing(sim.World.Store<Position>().Get(index).Value);
            counts[cell] = counts.GetValueOrDefault(cell) + 1;
        }

        return counts.Count == 0 ? 0 : counts.Values.Max();
    }

    private static List<FixVec2> Positions(GameSimulation sim) =>
        sim.World.Store<Creature>().EntityIndices.ToArray().Select(index => sim.World.Store<Position>().Get(index).Value).ToList();
}
