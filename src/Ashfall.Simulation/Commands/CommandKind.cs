namespace Ashfall.Simulation.Commands;

/// <summary>Player orders. Values are part of the replay and network format: append only, never renumber.</summary>
public enum CommandKind : byte
{
    None = 0,

    /// <summary>Move a unit to a point, ignoring enemies.</summary>
    Move = 1,

    /// <summary>Stop a unit: cancel movement and its current target.</summary>
    Stop = 2,

    /// <summary>Attack a specific enemy entity.</summary>
    Attack = 3,

    /// <summary>Move to a point, engaging enemies on the way.</summary>
    AttackMove = 4,

    /// <summary>Start constructing a structure at a cell.</summary>
    Build = 5,

    /// <summary>Queue a unit at a production structure.</summary>
    Train = 6,
}
