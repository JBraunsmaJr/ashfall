using Ashfall.Simulation;
using Ashfall.Simulation.Commands;

namespace Ashfall.Session.Commands;

/// <summary>Issues a fixed list of commands at given ticks. The basis of scenario tests.</summary>
public sealed class ScriptedCommandSource : ICommandSource
{
    private readonly List<ScheduledCommand> _script = [];
    private int _next;

    public ScriptedCommandSource Add(long tick, Command command)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        // Keep the script sorted by tick, preserving insertion order within a tick.
        var position = _script.Count;
        while (position > _next && _script[position - 1].Tick > tick)
        {
            position--;
        }

        _script.Insert(position, new ScheduledCommand(tick, command));
        return this;
    }

    public void CollectCommands(GameSimulation simulation, long tick, List<Command> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        // Commands scheduled for ticks that already ran are dropped, as late network turns would be.
        while (_next < _script.Count && _script[_next].Tick < tick)
        {
            _next++;
        }

        while (_next < _script.Count && _script[_next].Tick == tick)
        {
            output.Add(_script[_next].Command);
            _next++;
        }
    }

    private readonly record struct ScheduledCommand(long Tick, Command Command);
}
