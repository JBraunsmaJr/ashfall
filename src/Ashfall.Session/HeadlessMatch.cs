using System.Runtime.InteropServices;
using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Session.Ai;
using Ashfall.Session.Commands;
using Ashfall.Session.Maps;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;
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
    private readonly TimeProvider _timeProvider;

    private HeadlessMatch(MatchSpec spec, GameSpeed speed, TimeProvider timeProvider)
    {
        Spec = spec;
        _timeProvider = timeProvider;
        Clock = new SessionClock(speed, timeProvider);
        if (spec.Mode == MatchMode.Skirmish)
        {
            Simulation = CreateSkirmish(spec, _sources, out var localPlayer);
            LocalPlayer = localPlayer;
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

    /// <summary>Runs exactly one tick immediately, ignoring the clock.</summary>
    public void Step()
    {
        _tickCommands.Clear();
        foreach (var source in _sources)
        {
            source.CollectCommands(Simulation, Simulation.Tick, _tickCommands);
        }

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

    private static GameSimulation CreateSkirmish(MatchSpec spec, List<ICommandSource> sources, out LocalOrders? localPlayer)
    {
        localPlayer = null;
        var content = GameContent.Standard;
        PlayerSetup[] players = [new(Team: 0, BulwarkContent.FactionId), new(Team: 1, BulwarkContent.FactionId)];
        var simulation = new GameSimulation(spec.Seed, DuelMap.Create(), content, players);
        var yard = content.StructureIndex(BulwarkContent.ConstructionYard);
        for (var player = 0; player < players.Length; player++)
        {
            simulation.PlaceStructure(player, yard, DuelMap.StartOrigin(player));
            switch (spec.Controllers[player])
            {
                case PlayerController.ScriptedAi:
                    sources.Add(new ScriptedSkirmishAi(player, spec.Seed));
                    break;
                case PlayerController.Human:
                    localPlayer = new LocalOrders(player);
                    sources.Add(localPlayer);
                    break;
                case PlayerController.IdleAi:
                default:
                    break;
            }
        }

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
