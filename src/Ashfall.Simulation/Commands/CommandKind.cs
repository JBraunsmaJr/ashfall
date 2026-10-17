namespace Ashfall.Simulation.Commands;

/// <summary>Player orders. Values are part of the replay and network format: append only, never renumber.</summary>
public enum CommandKind : byte
{
    None = 0,
    Move = 1,
    Stop = 2,
}
