using Ashfall.Core;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Systems;

namespace Ashfall.Simulation;

/// <summary>
/// The authoritative, deterministic game state. Given the same seed, the same setup calls and the
/// same command list for every tick, every instance reaches a bit-identical state on any machine.
/// </summary>
public sealed class GameSimulation
{
    private readonly ISystem[] _systems;
    private Command[] _commandScratch = new Command[64];

    public GameSimulation(ulong seed, MapBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "Map must be at least one cell in each direction.");
        }

        Seed = seed;
        Bounds = bounds;
        Random = new DeterministicRandom(seed);
        World = new World();

        // Registration order is part of the state hash: append new component types at the end.
        World.Register<Position>();
        World.Register<Mover>();
        World.Register<Owner>();

        // System order is fixed and identical on every peer.
        _systems = [new MovementSystem()];
    }

    public ulong Seed { get; }

    public MapBounds Bounds { get; }

    /// <summary>Number of ticks simulated so far. The next call to <see cref="Step"/> runs this tick.</summary>
    public long Tick { get; private set; }

    public World World { get; }

    /// <summary>The match's single random stream. Only the simulation may draw from it.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>Commands rejected so far (dead unit, wrong owner, unknown kind). Part of the state hash.</summary>
    public long RejectedCommands { get; private set; }

    /// <summary>Creates a unit owned by <paramref name="playerId"/>. Call during setup, identically on every peer.</summary>
    public Entity SpawnUnit(int playerId, FixVec2 position, Fix64 speedPerTick)
    {
        var unit = World.Create();
        World.Add(unit, new Position(Bounds.Clamp(position)));
        World.Add(unit, Mover.Idle(speedPerTick));
        World.Add(unit, new Owner(playerId));
        return unit;
    }

    /// <summary>Advances one tick: applies this tick's commands, runs every system, then applies destruction.</summary>
    /// <param name="commands">All commands scheduled for this tick, from every player. Order between players does not matter.</param>
    public void Step(ReadOnlySpan<Command> commands)
    {
        foreach (var command in SortByPlayer(commands))
        {
            Apply(command);
        }

        var context = new TickContext(Tick, World, Random, Bounds);
        foreach (var system in _systems)
        {
            system.Update(context);
        }

        World.FlushDestroyed();
        Tick++;
    }

    /// <summary>Hash of the complete simulation state, compared between peers to detect desyncs.</summary>
    public ulong ComputeStateHash()
    {
        var hasher = new StateHasher();
        hasher.Add(Tick);
        hasher.Add(Seed);
        hasher.Add(Bounds.Width);
        hasher.Add(Bounds.Height);
        var random = Random.Snapshot;
        hasher.Add(random.State);
        hasher.Add(random.Increment);
        hasher.Add(RejectedCommands);
        World.HashInto(ref hasher);
        return hasher.Value;
    }

    private void Apply(in Command command)
    {
        if (!World.IsAlive(command.Unit)
            || !World.Has<Owner>(command.Unit)
            || World.Get<Owner>(command.Unit).PlayerId != command.PlayerId
            || !World.Has<Mover>(command.Unit))
        {
            RejectedCommands++;
            return;
        }

        ref var mover = ref World.Get<Mover>(command.Unit);
        switch (command.Kind)
        {
            case CommandKind.Move:
                mover.Target = Bounds.Clamp(command.Target);
                mover.HasTarget = true;
                break;
            case CommandKind.Stop:
                mover.HasTarget = false;
                break;
            case CommandKind.None:
            default:
                RejectedCommands++;
                break;
        }
    }

    // Stable insertion sort by player id: within one player, the order the player issued commands is kept.
    // Lockstep peers may receive different players' turns in different orders, so this makes them agree.
    private ReadOnlySpan<Command> SortByPlayer(ReadOnlySpan<Command> commands)
    {
        if (commands.Length > _commandScratch.Length)
        {
            _commandScratch = new Command[commands.Length * 2];
        }

        var sorted = _commandScratch.AsSpan(0, commands.Length);
        commands.CopyTo(sorted);
        for (var i = 1; i < sorted.Length; i++)
        {
            var current = sorted[i];
            var j = i - 1;
            while (j >= 0 && sorted[j].PlayerId > current.PlayerId)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }

            sorted[j + 1] = current;
        }

        return sorted;
    }
}
