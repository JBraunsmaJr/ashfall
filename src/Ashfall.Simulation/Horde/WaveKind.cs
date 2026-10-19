namespace Ashfall.Simulation.Horde;

/// <summary>What a wave the director sends is for (GDD section 3).</summary>
public enum WaveKind
{
    /// <summary>A small group of the brood testing the defences nearest the nest.</summary>
    Probe,

    /// <summary>The fast creatures of a brood sent at a player's refineries and the harvesters around them.</summary>
    Raid,

    /// <summary>Everything brooding, at a player's base; on harder difficulties split between two players.</summary>
    Assault,
}
