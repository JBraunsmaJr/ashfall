using System.Diagnostics;
using System.Globalization;
using Ashfall.Core;
using Ashfall.Session;
using Ashfall.Session.Maps;
using Ashfall.Simulation.Components;

namespace Ashfall.Headless;

/// <summary>
/// Runs the horde stress test (<see cref="MatchMode.Swarm"/>) one tick at a time, timing every tick, so the
/// GDD's swarm budget (5,000 creatures within 25 ms a tick) is measured rather than assumed.
/// </summary>
internal static class SwarmRunner
{
    /// <summary>The first second of game time is left out of the tick statistics: it is mostly JIT warm-up.</summary>
    private const int WarmupTicks = 15;

    public static SwarmRun Run(HeadlessMatch match, long ticks, CancellationToken cancellationToken)
    {
        var times = new List<double>((int)Math.Min(ticks, int.MaxValue));
        var fighting = new List<double>();
        var total = Stopwatch.StartNew();
        var tick = Stopwatch.StartNew();
        var firstTick = match.Tick;
        var fellAt = -1L;
        while (match.Tick - firstTick < ticks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engaged = IsUnderAttack(match);
            tick.Restart();
            match.Step();
            var elapsed = tick.Elapsed.TotalMilliseconds;
            if (match.Tick - firstTick > WarmupTicks)
            {
                times.Add(elapsed);
                if (engaged)
                {
                    fighting.Add(elapsed);
                }
            }

            if (fellAt < 0 && SwarmFortress.Survivors(match.Simulation, player: 0).Structures == 0)
            {
                fellAt = match.Tick;
            }
        }

        total.Stop();
        var result = new MatchResult(
            match.Tick,
            match.Simulation.ComputeStateHash(),
            StopReason.TickLimit,
            match.Tick - firstTick,
            total.Elapsed,
            match.Checkpoints.ToArray());
        return new SwarmRun(result, TickStats.From(times), TickStats.From(fighting), fellAt, match);
    }

    /// <summary>The mean tick time the budget is judged on: the worse of the whole run and the fighting.</summary>
    public static double BudgetedMean(SwarmRun run) => Math.Max(run.Stats.Mean, run.Fighting.Mean);

    // The heavy part of the test: some creature is attacking something of the fortress's.
    private static bool IsUnderAttack(HeadlessMatch match)
    {
        foreach (var creature in match.Simulation.World.Store<Creature>().Components)
        {
            if (!creature.Target.IsNone)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Timing of one tick: mean, 95th percentile and worst, in milliseconds.</summary>
    internal readonly record struct TickStats(double Mean, double P95, double Max)
    {
        public static TickStats From(List<double> times)
        {
            if (times.Count == 0)
            {
                return default;
            }

            times.Sort();
            return new TickStats(times.Average(), times[(int)Math.Min(times.Count - 1, Math.Ceiling(times.Count * 0.95) - 1)], times[^1]);
        }
    }

    internal sealed class SwarmRun(MatchResult result, TickStats stats, TickStats fighting, long fellAt, HeadlessMatch match)
    {
        public MatchResult Result { get; } = result;

        /// <summary>Every tick after warm-up.</summary>
        public TickStats Stats { get; } = stats;

        /// <summary>Ticks during which creatures were attacking the fortress: the heaviest load.</summary>
        public TickStats Fighting { get; } = fighting;

        /// <summary>The tick the fortress's last structure fell, or -1 if it stood to the end.</summary>
        public long FellAt { get; } = fellAt;

        /// <summary>Creatures, kills and the fortress's survivors at the end of the run.</summary>
        public string Describe()
        {
            var simulation = match.Simulation;
            var maw = simulation.Maw!;
            var (structures, units) = SwarmFortress.Survivors(simulation, player: 0);
            var nests = simulation.World.Store<Nest>();
            var biomass = 0;
            foreach (var nest in nests.Components)
            {
                biomass += nest.Biomass;
            }

            var breeding = match.Spec.Nests == 0 ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" nests={nests.Count}/{match.Spec.Nests} biomass={biomass} cindrite={simulation.Map.TotalCindrite}");
            return string.Create(
                CultureInfo.InvariantCulture,
                $" creatures={simulation.World.Store<Creature>().Count} peak={maw.PeakAlive} spawned={maw.Spawned} killed={maw.Killed}{breeding}" +
                $" fortress: structures={structures} defenders={units} {(FellAt < 0 ? "held" : $"fell at tick {FellAt}")}" +
                $" tick-ms: mean={Stats.Mean:F2} p95={Stats.P95:F2} max={Stats.Max:F2}" +
                $" fighting: mean={Fighting.Mean:F2} p95={Fighting.P95:F2} max={Fighting.Max:F2}");
        }
    }
}
