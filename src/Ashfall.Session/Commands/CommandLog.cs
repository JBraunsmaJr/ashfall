using Ashfall.Simulation.Commands;

namespace Ashfall.Session.Commands;

/// <summary>
/// Every command a person (local or remote) issued, by tick: with the match spec, enough to replay the
/// whole match, because AI commands are recomputed deterministically on replay.
/// </summary>
public sealed class CommandLog
{
    private readonly List<(long Tick, Command[] Commands)> _entries = [];

    public IReadOnlyList<(long Tick, Command[] Commands)> Entries => _entries;

    public int CommandCount { get; private set; }

    public void Record(long tick, ReadOnlySpan<Command> commands)
    {
        if (commands.IsEmpty)
        {
            return;
        }

        if (_entries.Count > 0 && _entries[^1].Tick >= tick)
        {
            throw new InvalidOperationException("Commands must be recorded in tick order.");
        }

        _entries.Add((tick, commands.ToArray()));
        CommandCount += commands.Length;
    }
}
