using Ashfall.Simulation;
using Ashfall.Simulation.Commands;

namespace Ashfall.Session.Commands;

/// <summary>
/// Commands from the local player (mouse, keyboard, sidebar), queued between ticks and issued on the
/// next one. Commands for any other player are refused: the queue speaks only for its own player.
/// </summary>
public sealed class LocalOrders(int playerId) : ICommandSource
{
    private readonly List<Command> _pending = [];

    public int PlayerId { get; } = playerId;

    /// <summary>Commands waiting for the next tick.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Total commands issued to the simulation so far.</summary>
    public long Issued { get; private set; }

    public void Enqueue(in Command command)
    {
        if (command.PlayerId != PlayerId)
        {
            throw new ArgumentException($"This queue issues commands for player {PlayerId} only, not player {command.PlayerId}.", nameof(command));
        }

        _pending.Add(command);
    }

    public void CollectCommands(GameSimulation simulation, long tick, List<Command> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.AddRange(_pending);
        Issued += _pending.Count;
        _pending.Clear();
    }
}
