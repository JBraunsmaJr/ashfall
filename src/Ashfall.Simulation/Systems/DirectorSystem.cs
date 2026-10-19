using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// The Maw's director (GDD section 3): the horde has no build order; this decides when what the nests have bred
/// goes, where, and how much of it. Only in matches with waves (<see cref="HordeSettings.WaveIntervalTicks"/>).
/// </summary>
/// <remarks>
/// <para><b>Assaults.</b> Once the breathing room after the last assault is over, the director calls an assault as
/// soon as the brood is three-quarters grown, or when the longest wait between assaults runs out. Everything brooding
/// goes, at the player hit least recently (the weakest on ties: fewest structures). On difficulties that split, a
/// big assault goes for two players at once: half the brood each, the half from the nests leaning toward a player
/// going to that player.</para>
/// <para><b>Probes and raids.</b> Between assaults, every harassment interval, a quarter of the brood probes the
/// defences of the next player in line, or (on difficulties with raids, half the time) the fast creatures of the
/// brood raid a player's refineries, where the harvesters are. Each takes the creatures whose nests are nearest
/// its target.</para>
/// <para><b>Telegraphs.</b> Nothing goes unannounced: a called wave is <see cref="MawDirector.Pending"/> for the
/// profile's warning (half for probes and raids), with where it will come from, before it launches. The creatures
/// are picked at launch, from whatever broods then.</para>
/// <para>Every choice is a pure function of simulation state and the Maw's random stream, so every peer agrees.</para>
/// </remarks>
public sealed class DirectorSystem : ISystem
{
    /// <summary>Brood needed before a probe or raid is worth sending.</summary>
    public const int HarassMinimum = 8;

    /// <summary>An assault at least this big splits, on difficulties that split.</summary>
    public const int SplitMinimum = 40;

    private readonly List<int> _brood = [];
    private readonly List<(long Distance, int Index)> _ranked = [];
    private int[] _structures = [];
    private Cell[] _homes = [];
    private bool[] _hasRefinery = [];
    private bool[] _homeIsYard = [];

    public void Update(in TickContext context)
    {
        var simulation = context.Simulation;
        var maw = simulation.Maw;
        if (maw?.Director is not { } director)
        {
            return;
        }

        if (director.Pending is { } order)
        {
            if (context.Tick >= order.LaunchTick)
            {
                Launch(context, director, order);
                director.Pending = null;
            }

            return;
        }

        if (context.Tick < maw.Settings.FirstSpawnTick)
        {
            return;
        }

        var brood = CountBrood(context);
        if (brood == 0)
        {
            return;
        }

        var profile = director.Profile;
        var assaultDue = context.Tick >= director.LastAssaultTick + maw.Settings.WaveIntervalTicks;
        if (context.Tick >= director.CooldownUntil && (brood * 4 >= maw.PopulationLimit * 3 || assaultDue))
        {
            Survey(context);
            var target = PickTarget(director, raid: false, exclude: -1);
            if (target >= 0)
            {
                var second = profile.SplitAssaults && brood >= SplitMinimum ? PickTarget(director, raid: false, exclude: target) : -1;
                director.Pending = new WaveOrder(WaveKind.Assault, target, second, SourceFor(context, target), context.Tick + profile.TelegraphTicks, brood);
            }

            return;
        }

        if (context.Tick < director.NextHarassTick || brood < HarassMinimum)
        {
            return;
        }

        director.NextHarassTick = context.Tick + profile.HarassIntervalTicks;
        Survey(context);
        var raidTarget = profile.Raids && maw.Random.Next(2) == 0 ? PickTarget(director, raid: true, exclude: -1) : -1;
        var lead = context.Tick + (profile.TelegraphTicks / 2);
        if (raidTarget >= 0 && CountFast(context) is var fast and > 0)
        {
            var size = AtLeast(brood / 3, HarassMinimum / 2);
            size = size < fast ? size : fast;
            director.Pending = new WaveOrder(WaveKind.Raid, raidTarget, -1, SourceFor(context, raidTarget), lead, size);
            return;
        }

        var probeTarget = PickTarget(director, raid: false, exclude: -1);
        if (probeTarget >= 0)
        {
            director.Pending = new WaveOrder(WaveKind.Probe, probeTarget, -1, SourceFor(context, probeTarget), lead, AtLeast(brood / 4, HarassMinimum / 2));
        }
    }

    // Sends the wave: picks its creatures from the brood, aims them, and moves the director's clocks on.
    private void Launch(in TickContext context, MawDirector director, WaveOrder order)
    {
        var world = context.World;
        var creatures = world.Store<Creature>();
        CountBrood(context);
        Survey(context);
        if (order.Kind == WaveKind.Assault)
        {
            // A split assault halves the brood: the half whose nests lie most toward the first target goes there.
            _ranked.Clear();
            foreach (var index in _brood)
            {
                var home = creatures.Get(index).Home;
                var lean = order.SecondTarget < 0 ? 0 : Lean(context, home, order.Target, order.SecondTarget);
                _ranked.Add((lean, index));
            }

            _ranked.Sort(ByDistanceThenIndex);
            var first = order.SecondTarget < 0 ? _ranked.Count : (_ranked.Count + 1) / 2;
            for (var i = 0; i < _ranked.Count; i++)
            {
                Send(ref creatures.Get(_ranked[i].Index), SwarmGoal.Base(i < first ? order.Target : order.SecondTarget));
            }

            director.Assaults++;
            director.LastAssaultTick = context.Tick;
            director.CooldownUntil = context.Tick + director.Profile.CooldownTicks;
        }
        else
        {
            // The nearest of the brood to the target go first (by their nest; then by entity index).
            _ranked.Clear();
            foreach (var index in _brood)
            {
                var creature = creatures.Get(index);
                if (order.Kind == WaveKind.Raid && !IsFast(context, creature.CreatureType))
                {
                    continue;
                }

                _ranked.Add((NestDistance(context, creature.Home, order.Target), index));
            }

            _ranked.Sort(ByDistanceThenIndex);
            var goal = order.Kind == WaveKind.Raid ? SwarmGoal.Refineries(order.Target) : SwarmGoal.Base(order.Target);
            for (var i = 0; i < _ranked.Count && i < order.Size; i++)
            {
                Send(ref creatures.Get(_ranked[i].Index), goal);
            }
        }

        director.Waves++;
        director.MarkTargeted(order.Target, context.Tick);
        if (order.SecondTarget >= 0)
        {
            director.MarkTargeted(order.SecondTarget, context.Tick);
        }
    }

    private static int ByDistanceThenIndex((long Distance, int Index) a, (long Distance, int Index) b) =>
        a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Index.CompareTo(b.Index);

    // How much nearer a nest is to the first player's home than to the second's (negative: nearer the first).
    private long Lean(in TickContext context, Entity home, int first, int second)
    {
        var toFirst = NestDistance(context, home, first);
        var toSecond = NestDistance(context, home, second);
        return toFirst == long.MaxValue || toSecond == long.MaxValue ? 0 : toFirst - toSecond;
    }

    private static int AtLeast(int value, int minimum) => value > minimum ? value : minimum;

    private static void Send(ref Creature creature, int goal)
    {
        creature.Brooding = false;
        creature.Goal = goal;
    }

    // Brooding creatures, in store order.
    private int CountBrood(in TickContext context)
    {
        _brood.Clear();
        var creatures = context.World.Store<Creature>();
        var indices = creatures.EntityIndices;
        var states = creatures.Components;
        for (var i = 0; i < states.Length; i++)
        {
            if (states[i].Brooding)
            {
                _brood.Add(indices[i]);
            }
        }

        return _brood.Count;
    }

    private int CountFast(in TickContext context)
    {
        var count = 0;
        var creatures = context.World.Store<Creature>();
        foreach (var index in _brood)
        {
            count += IsFast(context, creatures.Get(index).CreatureType) ? 1 : 0;
        }

        return count;
    }

    // Raiders are the Maw's fastest kind.
    private static bool IsFast(in TickContext context, int creatureType)
    {
        var content = context.Content;
        var fastest = Fix64.Zero;
        for (var i = 0; i < content.Creatures.Count; i++)
        {
            fastest = content.Creature(i).SpeedPerTick > fastest ? content.Creature(i).SpeedPerTick : fastest;
        }

        return content.Creature(creatureType).SpeedPerTick == fastest;
    }

    // Each player's standing structures (walls do not count), home (its Construction Yard, else its first structure)
    // and whether it has a refinery.
    private void Survey(in TickContext context)
    {
        var players = context.Players.Count;
        if (_structures.Length != players)
        {
            _structures = new int[players];
            _homes = new Cell[players];
            _hasRefinery = new bool[players];
            _homeIsYard = new bool[players];
        }

        Array.Clear(_structures);
        Array.Clear(_hasRefinery);
        Array.Clear(_homeIsYard);
        var world = context.World;
        var owners = world.Store<Owner>();
        var structures = world.Store<StructureInfo>();
        foreach (var index in structures.EntityIndices)
        {
            var player = owners.Get(index).PlayerId;
            if (player < 0 || player >= players)
            {
                continue;
            }

            var info = structures.Get(index);
            var role = context.Content.Structure(info.StructureType).Role;
            if (role == Content.StructureRole.Wall)
            {
                continue;
            }

            var middle = Middle(context, info);
            if (_structures[player] == 0 || (role == Content.StructureRole.ConstructionYard && !_homeIsYard[player]))
            {
                _homes[player] = middle;
                _homeIsYard[player] = role == Content.StructureRole.ConstructionYard;
            }

            _structures[player]++;
            _hasRefinery[player] |= role == Content.StructureRole.Refinery && info.IsComplete;
        }
    }

    // The player next in line: never hit or hit longest ago, then the fewest structures, then the lowest id.
    private int PickTarget(MawDirector director, bool raid, int exclude)
    {
        var best = -1;
        for (var player = 0; player < _structures.Length; player++)
        {
            if (player == exclude || _structures[player] == 0 || (raid && !_hasRefinery[player]))
            {
                continue;
            }

            if (best < 0
                || director.LastTargeted(player) < director.LastTargeted(best)
                || (director.LastTargeted(player) == director.LastTargeted(best) && _structures[player] < _structures[best]))
            {
                best = player;
            }
        }

        return best;
    }

    // Where a wave for this player emerges: the mouth of the nest nearest its home (lowest index on ties).
    private Cell SourceFor(in TickContext context, int player)
    {
        var world = context.World;
        var best = new Cell(-1, -1);
        var bestDistance = long.MaxValue;
        foreach (var index in world.Store<Nest>().EntityIndices)
        {
            var mouth = context.Simulation.ExitCell(world.Store<StructureInfo>().Get(index));
            var distance = DistanceSquared(mouth, _homes[player]);
            if (distance < bestDistance)
            {
                best = mouth;
                bestDistance = distance;
            }
        }

        return best;
    }

    // How far a creature's nest is from a player's home (a nest that has died counts as far away).
    private long NestDistance(in TickContext context, Entity home, int player)
    {
        var world = context.World;
        if (!world.IsAlive(home) || !world.Has<StructureInfo>(home))
        {
            return long.MaxValue;
        }

        return DistanceSquared(Middle(context, world.Get<StructureInfo>(home)), _homes[player]);
    }

    private static Cell Middle(in TickContext context, StructureInfo info)
    {
        var definition = context.Content.Structure(info.StructureType);
        return new Cell(info.Origin.X + (definition.Width / 2), info.Origin.Y + (definition.Height / 2));
    }

    private static long DistanceSquared(Cell a, Cell b) => ((long)(a.X - b.X) * (a.X - b.X)) + ((long)(a.Y - b.Y) * (a.Y - b.Y));
}
