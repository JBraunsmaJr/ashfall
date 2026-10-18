namespace Ashfall.Simulation.Vision;

/// <summary>What a player knows about a cell (GDD section 2: black shroud, grey fog, clear vision).</summary>
public enum CellVisibility : byte
{
    /// <summary>Never seen: black shroud.</summary>
    Shroud = 0,

    /// <summary>Seen before but not now: terrain and structures as last seen, no units.</summary>
    Fog = 1,

    /// <summary>In sight of one of the team's units or structures right now.</summary>
    Visible = 2,
}
