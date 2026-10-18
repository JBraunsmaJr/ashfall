using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>The rules a co-op Purge adds: brooding, waves that escalate, nests guarded by their brood, and the Purge victory.</summary>
public class PurgeRuleTests
{
    private const int WaveInterval = 300;

    private static int Pit => TestMatch.Content.StructureIndex(MawContent.SpawningPit);

    [Fact]
    public void Nests_brood_until_the_wave_then_the_brood_marches_and_the_next_wave_is_bigger()
    {
        var sim = WaveMatch(out _, out var nest);
        var mouth = sim.ExitCell(sim.World.Get<StructureInfo>(nest));

        Run(sim, WaveInterval - 10);
        var maw = sim.Maw!;
        Assert.Equal(0, maw.Waves);
        Assert.Equal(20, maw.PopulationLimit);
        Assert.Equal(20, sim.World.Store<Creature>().Count); // bred up to the first wave's size, and no further
        foreach (var index in sim.World.Store<Creature>().EntityIndices)
        {
            Assert.True(sim.World.Store<Creature>().Get(index).Brooding);
            var cell = Cell.Containing(sim.World.Store<Position>().Get(index).Value);
            Assert.InRange(Math.Max(Math.Abs(cell.X - mouth.X), Math.Abs(cell.Y - mouth.Y)), 0, 5);
        }

        Assert.True(maw.IsWaveImminent(sim.Tick));
        var bredBefore = maw.Spawned;
        RunUntil(sim, s => s.Maw!.Waves == 1, 20);
        Assert.Equal(WaveInterval + 1, sim.Tick); // launched while tick 300 ran
        Assert.Equal(2 * WaveInterval, maw.NextWaveTick);
        Assert.Equal(30, maw.PopulationLimit);

        // Everything bred before the launch marches; only what was bred since broods.
        Assert.Equal(maw.Spawned - bredBefore, sim.World.Store<Creature>().Components.ToArray().Count(creature => creature.Brooding));

        // The wave closes on the base (bottom left); the nest starts a new brood behind it.
        Run(sim, 150);
        var marching = 0;
        var brooding = 0;
        foreach (var index in sim.World.Store<Creature>().EntityIndices)
        {
            var creature = sim.World.Store<Creature>().Get(index);
            if (creature.Brooding)
            {
                brooding++;
                continue;
            }

            marching++;
            Assert.True(sim.World.Store<Position>().Get(index).Value.X < Fix64.FromInt(mouth.X - 6), "The wave should have left the nest.");
        }

        Assert.True(marching > 0);
        Assert.InRange(brooding, 1, 10);
    }

    [Fact]
    public void A_brood_guards_its_nest_against_units_beyond_its_own_reach()
    {
        var sim = WaveMatch(out _, out var nest);
        Run(sim, 120); // a brood gathers below the nest
        var center = sim.World.Get<Position>(nest).Value;

        // A rifleman above the nest: more than a Skitter's aggro range from every member of the brood below it.
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), center + new FixVec2(Fix64.Zero, Fix64.FromInt(-7)));
        var health = sim.World.Get<Health>(rifleman).Max;
        RunUntil(sim, s => !s.World.IsAlive(rifleman) || s.World.Get<Health>(rifleman).Current < health, 200);
        Assert.Equal(0, sim.Maw!.Waves);
    }

    [Fact]
    public void A_brood_whose_nest_dies_marches_at_once()
    {
        var sim = WaveMatch(out _, out var nest);
        Run(sim, 120);
        Assert.NotEmpty(sim.World.Store<Creature>().EntityIndices.ToArray());

        sim.World.Get<Health>(nest).Current = 0;
        Run(sim, 2);

        Assert.False(sim.World.IsAlive(nest));
        Assert.All(sim.World.Store<Creature>().Components.ToArray(), creature => Assert.False(creature.Brooding));
    }

    [Fact]
    public void The_players_win_a_purge_when_the_last_nest_falls()
    {
        var sim = WaveMatch(out _, out var nest, victory: true);
        var second = sim.PlaceNest(Pit, new Cell(36, 8));
        Run(sim, 10);
        Assert.False(sim.IsOver); // one team, but nests remain: a Purge does not end on "last team standing"

        sim.World.Get<Health>(nest).Current = 0;
        Run(sim, 2);
        Assert.False(sim.IsOver);

        sim.World.Get<Health>(second).Current = 0;
        Run(sim, 2);
        Assert.True(sim.IsOver);
        Assert.Equal(0, sim.WinningTeam);
    }

    [Fact]
    public void The_maw_wins_a_purge_when_every_player_has_lost_their_base()
    {
        var sim = WaveMatch(out var plant, out _, victory: true);
        sim.World.Get<Health>(plant).Current = 0;
        Run(sim, 2);

        Assert.True(sim.IsOver);
        Assert.Equal(GameSimulation.MawTeam, sim.WinningTeam);
        Assert.True(sim.Players[0].Defeated);
    }

    [Fact]
    public void A_unit_that_picked_a_building_turns_on_an_enemy_that_comes_into_range()
    {
        var sim = Create();
        var plant = sim.PlaceStructure(1, Structure(BulwarkContent.PowerPlant), new Cell(14, 14));
        var rifleman = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(12, 12).Center);
        RunUntil(sim, s => s.World.Get<Armament>(rifleman).Target == plant, 30);

        var enemy = sim.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new Cell(9, 12).Center);
        RunUntil(sim, s => s.World.Get<Armament>(rifleman).Target == enemy, 30);
    }

    // One player with a Power Plant in the bottom-left corner; a fed Spawning Pit top right; waves every 300 ticks
    // from tick 0, starting at 20 creatures and growing by 10.
    private static GameSimulation WaveMatch(out Entity plant, out Entity nest, bool victory = false)
    {
        var horde = new HordeSettings
        {
            CreatureType = 0,
            Population = 500,
            EdgeSpawns = false,
            WaveIntervalTicks = WaveInterval,
            FirstWaveSize = 20,
            WaveGrowth = 10,
        };
        var sim = new GameSimulation(7, new GridMap(48, 48), TestMatch.Content, [new PlayerSetup(0, BulwarkContent.FactionId)],
            victory, horde, goal: MatchGoal.Purge);
        plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(4, 40));
        nest = sim.PlaceNest(Pit, new Cell(36, 20));
        for (var y = 14; y <= 30; y++)
        {
            for (var x = 30; x <= 44; x++)
            {
                var cell = new Cell(x, y);
                if (sim.Map.StructureAt(cell) < 0)
                {
                    sim.Map.SetCindrite(cell, 400);
                }
            }
        }

        return sim;
    }
}
