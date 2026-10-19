using System.Runtime.InteropServices;
using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Session.Ai;
using Ashfall.Session.Commands;
using Ashfall.Session.Maps;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Players;

namespace Ashfall.Session;

/// <summary>
/// Hosts one match without a window or GPU: builds the simulation from a <see cref="MatchSpec"/>,
/// gathers commands from every <see cref="ICommandSource"/> each tick, paces ticks with a
/// <see cref="SessionClock"/> and records a hash checkpoint every
/// <see cref="SimulationConstants.HashCheckInterval"/> ticks. The client wraps the same host.
/// </summary>
public sealed class HeadlessMatch : IAsyncDisposable
{
    private readonly List<ICommandSource> _sources = [];
    private readonly List<Command> _tickCommands = [];
    private readonly List<HashCheckpoint> _checkpoints = [];
    private readonly List<Command> _recorded = [];
    private readonly TimeProvider _timeProvider;

    /// <summary>The skirmish or Purge map (hand-made or generated); null for the proving ground and the swarm test.</summary>
    public MatchMap? Map { get; }

    /// <summary>Nests the Maw started with (Purge and swarm; 0 otherwise): with those left, how many the players destroyed.</summary>
    public int NestsAtStart { get; }

    private HeadlessMatch(MatchSpec spec, GameSpeed speed, TimeProvider timeProvider)
    {
        Spec = spec;
        _timeProvider = timeProvider;
        Clock = new SessionClock(speed, timeProvider);
        if (spec.Mode is MatchMode.Skirmish or MatchMode.Purge)
        {
            Simulation = spec.Mode == MatchMode.Purge
                ? CreatePurge(spec, _sources, out var localPlayer, out var map)
                : CreateSkirmish(spec, _sources, out localPlayer, out map);
            LocalPlayer = localPlayer;
            Map = map;
            NestsAtStart = Simulation.World.Store<Nest>().Count;
            var remote = Enumerable.Range(0, spec.Players).Where(seat => spec.Controllers[seat] == PlayerController.Remote).ToArray();
            if (remote.Length > 0)
            {
                Turns = new TurnFeed(remote, spec.Players, spec.InputDelay);
                _sources.Insert(0, Turns);
            }

            return;
        }

        if (spec.Mode == MatchMode.Swarm)
        {
            Simulation = CreateSwarm(spec);
            NestsAtStart = Simulation.World.Store<Nest>().Count;
            if (spec.Controllers[0] == PlayerController.Human)
            {
                LocalPlayer = new LocalOrders(0);
                _sources.Add(LocalPlayer);
            }
            else if (spec.Controllers[0] == PlayerController.Remote)
            {
                Turns = new TurnFeed([0], spec.Players, spec.InputDelay); // a replay of someone holding the fortress
                _sources.Insert(0, Turns);
            }

            return;
        }

        Simulation = new GameSimulation(spec.Seed, spec.Map);
        SpawnStartingUnits();

        for (var player = 0; player < spec.Players; player++)
        {
            if (spec.OrderIntervalTicks > 0 && spec.OrdersPerInterval > 0)
            {
                _sources.Add(new WanderOrdersSource(player, spec.Seed, spec.OrderIntervalTicks, spec.OrdersPerInterval));
            }
        }
    }

    public MatchSpec Spec { get; }

    public GameSimulation Simulation { get; }

    public SessionClock Clock { get; }

    /// <summary>The human player's command queue, or null when every side is an AI.</summary>
    public LocalOrders? LocalPlayer { get; }

    /// <summary>Turns for <see cref="PlayerController.Remote"/> seats (network players, replays), or null if there are none.</summary>
    public TurnFeed? Turns { get; }

    /// <summary>Every command from people (the local player and remote seats), for replays and desync dumps.</summary>
    public CommandLog Log { get; } = new();

    public long Tick => Simulation.Tick;

    /// <summary>True once the simulation has declared a result (skirmish only).</summary>
    public bool IsOver => Simulation.IsOver;

    /// <summary>The winning team, or -1 while the match runs or after a draw.</summary>
    public int WinningTeam => Simulation.WinningTeam;

    public IReadOnlyList<HashCheckpoint> Checkpoints => _checkpoints;

    /// <summary>Creates a match ready to run from tick 0.</summary>
    public static HeadlessMatch Start(MatchSpec spec, GameSpeed speed, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        spec.Validate();
        return new HeadlessMatch(spec, speed, timeProvider ?? TimeProvider.System);
    }

    /// <summary>Async form of <see cref="Start"/>; network sessions will need to await peers here.</summary>
    public static ValueTask<HeadlessMatch> StartAsync(MatchSpec spec, GameSpeed speed, TimeProvider? timeProvider = null) =>
        ValueTask.FromResult(Start(spec, speed, timeProvider));

    /// <summary>Adds a source of commands (a scripted test, an AI, a player).</summary>
    public HeadlessMatch AddCommandSource(ICommandSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
        return this;
    }

    /// <summary>Changes game speed; takes effect from the next tick.</summary>
    public void SetSpeed(GameSpeed speed) => Clock.Restart(speed, Tick);

    /// <summary>True when the next tick has every turn it needs (always, without remote seats).</summary>
    public bool CanStep => Turns is null || Turns.IsComplete(Simulation.Tick);

    /// <summary>Runs exactly one tick immediately, ignoring the clock.</summary>
    public void Step()
    {
        _tickCommands.Clear();
        _recorded.Clear();
        foreach (var source in _sources)
        {
            var before = _tickCommands.Count;
            source.CollectCommands(Simulation, Simulation.Tick, _tickCommands);
            if (source is LocalOrders or TurnFeed)
            {
                _recorded.AddRange(CollectionsMarshal.AsSpan(_tickCommands)[before..]);
            }
        }

        Log.Record(Simulation.Tick, CollectionsMarshal.AsSpan(_recorded));
        Simulation.Step(CollectionsMarshal.AsSpan(_tickCommands));

        if (Simulation.Tick % SimulationConstants.HashCheckInterval == 0)
        {
            _checkpoints.Add(new HashCheckpoint(Simulation.Tick, Simulation.ComputeStateHash()));
        }
    }

    /// <summary>
    /// Runs ticks at the clock's speed until <paramref name="stopWhen"/> returns true or
    /// <paramref name="maxTicks"/> ticks have run in this call. Limits are in ticks, never seconds,
    /// so results do not depend on machine speed.
    /// </summary>
    public async Task<MatchResult> RunUntilAsync(
        Func<HeadlessMatch, bool> stopWhen,
        long maxTicks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stopWhen);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTicks);

        var started = _timeProvider.GetTimestamp();
        var firstTick = Simulation.Tick;
        Clock.Restart(Clock.Speed, firstTick);
        var reason = StopReason.TickLimit;

        while (Simulation.Tick - firstTick < maxTicks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopWhen(this))
            {
                reason = StopReason.ConditionMet;
                break;
            }

            await Clock.WaitUntilDueAsync(Simulation.Tick, cancellationToken).ConfigureAwait(false);
            Step();
        }

        if (reason == StopReason.TickLimit && stopWhen(this))
        {
            reason = StopReason.ConditionMet;
        }

        return new MatchResult(
            Simulation.Tick,
            Simulation.ComputeStateHash(),
            reason,
            Simulation.Tick - firstTick,
            _timeProvider.GetElapsedTime(started),
            _checkpoints.ToArray(),
            Simulation.IsOver,
            Simulation.WinningTeam);
    }

    /// <summary>Runs exactly <paramref name="ticks"/> ticks at the clock's speed.</summary>
    public Task<MatchResult> RunTicksAsync(long ticks, CancellationToken cancellationToken = default) =>
        RunUntilAsync(static _ => false, ticks, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Runs until the simulation declares a result or <paramref name="maxTicks"/> ticks have run.</summary>
    public Task<MatchResult> RunToCompletionAsync(long maxTicks, CancellationToken cancellationToken = default) =>
        RunUntilAsync(static match => match.IsOver, maxTicks, cancellationToken);

    private static GameSimulation CreateSkirmish(MatchSpec spec, List<ICommandSource> sources, out LocalOrders? localPlayer, out MatchMap matchMap)
    {
        var content = GameContent.Standard;
        var players = new PlayerSetup[spec.Players];
        for (var player = 0; player < players.Length; player++)
        {
            players[player] = new PlayerSetup(spec.Teams[player], BulwarkContent.FactionId);
        }

        var map = matchMap = MatchMap.Create(spec.GeneratedMap, spec.Players);
        var simulation = new GameSimulation(spec.Seed, map.Grid, content, players, cindriteRegrowth: true);
        PlaceBases(spec, simulation, map, sources, out localPlayer);
        return simulation;
    }

    // Co-op against the Maw: the players' bases together in the team area of a co-op map, Spawning Pits on the
    // fields away from them, and a horde that breeds only from the nests.
    private static GameSimulation CreatePurge(MatchSpec spec, List<ICommandSource> sources, out LocalOrders? localPlayer, out MatchMap matchMap)
    {
        var content = GameContent.Standard;
        var players = new PlayerSetup[spec.Players];
        for (var player = 0; player < players.Length; player++)
        {
            players[player] = new PlayerSetup(spec.Teams[player], BulwarkContent.FactionId, spec.BonusCredits);
        }

        var map = matchMap = MatchMap.Create(spec.GeneratedMap, spec.Players, coop: true);
        var horde = new HordeSettings
        {
            CreatureType = content.CreatureIndex(MawContent.Skitter),
            Population = spec.Creatures,
            SpawnPerTick = 50,
            MinimumDistance = PurgeNests.MinimumDistance,
            EdgeSpawns = false,
            FirstSpawnTick = MatchSpec.PurgeGraceTicks,
            WaveIntervalTicks = MatchSpec.PurgeWaveIntervalTicks,
            FirstWaveSize = MatchSpec.PurgeFirstWaveSize(spec.Players),
            WaveGrowth = MatchSpec.PurgeWaveGrowth(spec.Players),
            Difficulty = spec.Difficulty,
        };
        var simulation = new GameSimulation(spec.Seed, map.Grid, content, players, victoryEnabled: true, horde, cindriteRegrowth: true, MatchGoal.Purge);
        var yards = PlaceBases(spec, simulation, map, sources, out localPlayer);
        var harvester = content.UnitIndex(BulwarkContent.Harvester);
        foreach (var yard in yards)
        {
            for (var i = 0; i < spec.BonusHarvesters; i++)
            {
                simulation.SpawnUnitAt(yard, harvester);
            }
        }

        var starts = map.StartOrigins.Take(spec.Players).ToArray();
        var nest = content.StructureIndex(spec.NestTier switch { 3 => MawContent.BehemothDen, 2 => MawContent.BroodNest, _ => MawContent.SpawningPit });
        if (PurgeNests.Place(simulation, starts, spec.Nests, nest) == 0)
        {
            throw new InvalidOperationException($"The map {map.Name} has no Cindrite field far enough from the bases for a nest.");
        }

        return simulation;
    }

    // Every player's Construction Yard at its start, and the command source for whoever plays it. Returns the yards.
    private static Entity[] PlaceBases(MatchSpec spec, GameSimulation simulation, MatchMap map, List<ICommandSource> sources, out LocalOrders? localPlayer)
    {
        localPlayer = null;
        var yard = simulation.Content.StructureIndex(BulwarkContent.ConstructionYard);
        var yards = new Entity[spec.Players];
        for (var player = 0; player < spec.Players; player++)
        {
            yards[player] = simulation.PlaceStructure(player, yard, map.StartOrigins[player]);
            switch (spec.Controllers[player])
            {
                case PlayerController.ScriptedAi:
                    sources.Add(new ScriptedSkirmishAi(player, spec.Seed));
                    break;
                case PlayerController.Human:
                    localPlayer = new LocalOrders(player);
                    sources.Add(localPlayer);
                    break;
                case PlayerController.Remote:
                case PlayerController.IdleAi:
                default:
                    break;
            }
        }

        return yards;
    }

    // One Bulwark fortress (player 0, idle) against the Maw: an edge spawner that keeps spec.Creatures alive, or
    // spec.Nests Spawning Pits on regrowing Cindrite fields, breeding up to that many. No victory condition: the run
    // lasts a fixed number of ticks.
    private static GameSimulation CreateSwarm(MatchSpec spec)
    {
        var content = GameContent.Standard;
        var horde = new HordeSettings
        {
            CreatureType = content.CreatureIndex(MawContent.Skitter),
            Population = spec.Creatures,
            SpawnPerTick = 50,
            MinimumDistance = 24,
            EdgeSpawns = spec.Nests == 0,
        };
        var simulation = new GameSimulation(
            spec.Seed, SwarmFortress.Create(), content, [new PlayerSetup(spec.Teams[0], BulwarkContent.FactionId)], victoryEnabled: false, horde,
            cindriteRegrowth: true);
        SwarmFortress.Build(simulation, player: 0);
        SwarmFortress.PlaceNests(simulation, spec.Nests);
        return simulation;
    }

    private void SpawnStartingUnits()
    {
        // Each player starts in its own vertical band of the map; positions come from the simulation's
        // random stream, so every peer places units identically.
        var random = Simulation.Random;
        var bounds = Simulation.Bounds;
        var bandWidth = Fix64.FromRatio(bounds.Width, Spec.Players);
        for (var player = 0; player < Spec.Players; player++)
        {
            var left = bandWidth * player;
            for (var unit = 0; unit < Spec.UnitsPerPlayer; unit++)
            {
                var position = new FixVec2(
                    random.NextFix64(left, left + bandWidth),
                    random.NextFix64(Fix64.Zero, Fix64.FromInt(bounds.Height)));
                Simulation.SpawnUnit(player, position, Spec.UnitSpeedPerTick);
            }
        }
    }
}
