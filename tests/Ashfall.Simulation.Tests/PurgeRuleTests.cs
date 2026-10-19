using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using Ashfall.Simulation.Systems;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>The rules a co-op Purge adds: brooding, waves that escalate, nests guarded by their brood, and the Purge victory.</summary>
public class PurgeRuleTests
{
    private const int WaveInterval = 300;

    private static int Pit => TestMatch.Content.StructureIndex(MawContent.SpawningPit);

    [Fact]
    public void The_director_telegraphs_an_assault_once_the_brood_has_grown_then_everything_brooding_marches()
    {
        var sim = WaveMatch(out _, out var nest);
        var mouth = sim.ExitCell(sim.World.Get<StructureInfo>(nest));
        var maw = sim.Maw!;
        var director = maw.Director!;
        Assert.Equal(20, maw.PopulationLimit); // the first wave's size, at Veteran's full budget

        // Nothing is called in the first half interval; then an assault, as soon as the brood is three-quarters grown.
        RunUntil(sim, s => s.Maw!.Director!.Pending is not null, 2 * WaveInterval);
        var called = sim.Tick - 1;
        var order = director.Pending!.Value;
        Assert.True(called >= WaveInterval / 2);
        Assert.Equal(WaveKind.Assault, order.Kind);
        Assert.Equal((0, -1), (order.Target, order.SecondTarget)); // one player: no split
        Assert.Equal(mouth, order.Source);
        Assert.Equal(called + director.Profile.TelegraphTicks, order.LaunchTick);
        Assert.InRange(order.Size, 15, 20);

        // The brood waits out the warning around its nest...
        Run(sim, director.Profile.TelegraphTicks - 5);
        Assert.Equal(0, director.Waves);
        Assert.All(sim.World.Store<Creature>().Components.ToArray(), creature => Assert.True(creature.Brooding));
        foreach (var index in sim.World.Store<Creature>().EntityIndices)
        {
            var cell = Cell.Containing(sim.World.Store<Position>().Get(index).Value);
            Assert.InRange(Math.Max(Math.Abs(cell.X - mouth.X), Math.Abs(cell.Y - mouth.Y)), 0, 5);
        }

        // ...then all of it goes, at the player's base, and the next assault may grow bigger after the breathing room.
        var bredBefore = maw.Spawned;
        RunUntil(sim, s => s.Maw!.Director!.Waves == 1, 10);
        Assert.Equal(order.LaunchTick + 1, sim.Tick);
        Assert.Null(director.Pending);
        Assert.Equal(1, director.Assaults);
        Assert.Equal(order.LaunchTick, director.LastAssaultTick);
        Assert.Equal(order.LaunchTick + director.Profile.CooldownTicks, director.CooldownUntil);
        Assert.Equal(30, maw.PopulationLimit);
        Assert.Equal(order.LaunchTick, director.LastTargeted(0));
        Assert.Equal(maw.Spawned - bredBefore, sim.World.Store<Creature>().Components.ToArray().Count(creature => creature.Brooding));
        Assert.All(
            sim.World.Store<Creature>().Components.ToArray().Where(creature => !creature.Brooding),
            creature => Assert.Equal(SwarmGoal.Base(0), creature.Goal));

        // The wave closes on the base (bottom left).
        Run(sim, 150);
        foreach (var index in sim.World.Store<Creature>().EntityIndices)
        {
            if (!sim.World.Store<Creature>().Get(index).Brooding)
            {
                Assert.True(sim.World.Store<Position>().Get(index).Value.X < Fix64.FromInt(mouth.X - 6), "The wave should have left the nest.");
            }
        }
    }

    [Fact]
    public void Between_assaults_the_director_probes_and_raids_and_every_wave_is_telegraphed()
    {
        // Two players: one with a refinery (a raid target), one without. A long match, watching every wave called.
        var sim = WaveMatch(out _, out _, players: 2);
        sim.PlaceStructure(1, Structure(BulwarkContent.Refinery), new Cell(30, 40));
        var director = sim.Maw!.Director!;
        var orders = new List<(long Called, WaveOrder Order)>();
        for (var tick = 0; tick < 6_000; tick++)
        {
            // The bases hold (their buildings are mended every tick), so the director keeps working for the whole run.
            Mended(sim);
            var before = director.Pending;
            sim.Step([]);
            if (director.Pending is { } order && order != before)
            {
                orders.Add((sim.Tick - 1, order));
            }
        }

        var seen = string.Join("; ", orders.Select(entry => $"{entry.Called}: {entry.Order.Kind} at {entry.Order.Target} x{entry.Order.Size}"));
        foreach (var kind in Enum.GetValues<WaveKind>())
        {
            Assert.True(orders.Any(entry => entry.Order.Kind == kind), $"No {kind}: {seen}");
        }

        foreach (var (called, order) in orders)
        {
            var lead = order.Kind == WaveKind.Assault ? director.Profile.TelegraphTicks : director.Profile.TelegraphTicks / 2;
            Assert.Equal(called + lead, order.LaunchTick);
            if (order.Kind == WaveKind.Raid)
            {
                Assert.Equal(1, order.Target); // only player 1 has a refinery
            }
        }

        // Waves take turns: a player is not hit twice running while the other is left alone.
        Assert.Contains(orders, entry => entry.Order.Target == 0);
        Assert.Contains(orders, entry => entry.Order.Target == 1);
        for (var i = 1; i < orders.Count; i++)
        {
            var previous = orders[i - 1].Order;
            var other = 1 - orders[i].Order.Target;
            var otherWasHit = previous.Target == other || previous.SecondTarget == other;
            Assert.False(previous.Target == orders[i].Order.Target && !otherWasHit && orders[i].Order.Kind != WaveKind.Raid, seen);
        }
    }

    [Fact]
    public void A_big_assault_splits_between_two_players_on_veteran_and_not_on_recruit()
    {
        foreach (var (difficulty, split) in new[] { (MawDifficulty.Veteran, true), (MawDifficulty.Recruit, false) })
        {
            var sim = WaveMatch(out _, out _, players: 2, firstWave: 70, difficulty: difficulty, interval: 3_000);
            RunUntil(sim, s => Mended(s).Maw!.Director!.Pending is { Kind: WaveKind.Assault }, 3_000);
            var order = sim.Maw!.Director!.Pending!.Value;
            Assert.Equal(split, order.SecondTarget >= 0);
            Assert.True(!split || order.Size >= DirectorSystem.SplitMinimum);

            // The brood just before the launch, and where each of it goes.
            RunUntil(sim, s => Mended(s).Tick == order.LaunchTick, sim.Maw.Director.Profile.TelegraphTicks + 2);
            var brood = sim.World.Store<Creature>().EntityIndices.ToArray().Where(index => sim.World.Store<Creature>().Get(index).Brooding).ToArray();
            Mended(sim).Step([]);
            Assert.Equal(1, sim.Maw.Director.Assaults);
            var goals = brood.GroupBy(index => sim.World.Store<Creature>().Get(index).Goal).ToDictionary(group => group.Key, group => group.Count());
            if (split)
            {
                Assert.Equal(2, goals.Count);
                Assert.InRange(goals[SwarmGoal.Base(order.Target)] - goals[SwarmGoal.Base(order.SecondTarget)], 0, 1); // halves
            }
            else
            {
                Assert.Equal([SwarmGoal.Base(order.Target)], goals.Keys);
            }
        }
    }

    [Fact]
    public void Harder_difficulties_grow_bigger_broods_and_warn_less()
    {
        var profiles = Enum.GetValues<MawDifficulty>().Select(DirectorProfile.For).ToArray();
        for (var i = 1; i < profiles.Length; i++)
        {
            Assert.True(profiles[i].BudgetPercent > profiles[i - 1].BudgetPercent);
            Assert.True(profiles[i].TelegraphTicks < profiles[i - 1].TelegraphTicks);
            Assert.True(profiles[i].CooldownTicks < profiles[i - 1].CooldownTicks);
        }

        Assert.Equal(30 * 15, profiles[0].TelegraphTicks); // the GDD's 30 seconds on Recruit...
        Assert.Equal(15 * 15, profiles[^1].TelegraphTicks); // ...and 15 on Extinction
        Assert.False(profiles[0].Raids);
    }

    [Fact]
    public void A_wave_for_a_player_with_nothing_left_of_its_goal_goes_for_the_nearest_structure()
    {
        var sim = WaveMatch(out var plant, out _, players: 2);
        var other = sim.World.EntityAt(sim.Map.StructureAt(new Cell(40, 42)));
        RunUntil(sim, s => s.Maw!.Director!.Waves == 1, 2 * WaveInterval + 400);
        var target = sim.World.Store<Creature>().Components.ToArray().First(creature => !creature.Brooding).Goal;
        var victim = SwarmGoal.PlayerOf(target) == 0 ? plant : other;

        sim.World.Get<Health>(victim).Current = 0;
        Run(sim, 3);
        Assert.All(
            sim.World.Store<Creature>().Components.ToArray().Where(creature => !creature.Brooding),
            creature => Assert.Equal(SwarmGoal.Nearest, creature.Goal));
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
        Assert.Equal(0, sim.Maw!.Director!.Waves);
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

    // Every building back at full health: a base that cannot fall, for watching the director over a long run.
    private static GameSimulation Mended(GameSimulation sim)
    {
        foreach (var index in sim.World.Store<StructureInfo>().EntityIndices)
        {
            ref var health = ref sim.World.Store<Health>().Get(index);
            health.Current = health.Max;
        }

        return sim;
    }

    // A Power Plant in the bottom-left corner (and, with two players, one bottom right for the second); a fed Spawning Pit
    // top right; a director whose assaults may come every 300 ticks, starting at 20 creatures and growing by 10.
    private static GameSimulation WaveMatch(
        out Entity plant,
        out Entity nest,
        bool victory = false,
        int players = 1,
        int firstWave = 20,
        MawDifficulty difficulty = MawDifficulty.Veteran,
        int interval = WaveInterval)
    {
        var horde = new HordeSettings
        {
            CreatureType = 0,
            Population = 500,
            EdgeSpawns = false,
            WaveIntervalTicks = interval,
            FirstWaveSize = firstWave,
            WaveGrowth = 10,
            Difficulty = difficulty,
        };
        var setups = Enumerable.Range(0, players).Select(player => new PlayerSetup(player, BulwarkContent.FactionId)).ToArray();
        var sim = new GameSimulation(7, new GridMap(48, 48), TestMatch.Content, setups, victory, horde, goal: MatchGoal.Purge);
        plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(4, 40));
        if (players > 1)
        {
            sim.PlaceStructure(1, Structure(BulwarkContent.PowerPlant), new Cell(40, 42));
        }

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
