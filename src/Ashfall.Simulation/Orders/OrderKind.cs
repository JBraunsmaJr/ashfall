namespace Ashfall.Simulation.Orders;

/// <summary>A unit's standing order. Values are part of the replay format: append only.</summary>
public enum OrderKind : byte
{
    /// <summary>Hold position; engage enemies that come within acquisition range.</summary>
    Idle = 0,

    /// <summary>Go to a point, ignoring enemies on the way.</summary>
    Move = 1,

    /// <summary>Destroy a specific target, chasing it.</summary>
    Attack = 2,

    /// <summary>Go to a point, engaging enemies met on the way.</summary>
    AttackMove = 3,

    /// <summary>Walk to a bunker (<see cref="Components.UnitOrder.Target"/>) and take shelter in it.</summary>
    Enter = 4,
}
