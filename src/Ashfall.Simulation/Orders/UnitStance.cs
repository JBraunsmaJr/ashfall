namespace Ashfall.Simulation.Orders;

/// <summary>
/// How far an idle unit goes after enemies on its own (an explicit attack or attack-move always pursues). Values are
/// part of the replay format: append only.
/// </summary>
public enum UnitStance : byte
{
    /// <summary>Engage what comes within acquisition range, chase it a short way, then come back to where it stood.</summary>
    Guard = 0,

    /// <summary>Never move to engage: fire at whatever comes within weapon range.</summary>
    HoldGround = 1,

    /// <summary>Engage what comes within acquisition range and pursue it wherever it goes.</summary>
    Free = 2,
}
