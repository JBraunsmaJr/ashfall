namespace Ashfall.Simulation.Map;

/// <summary>Ground type of a cell. Height levels, ramps and water arrive with map generation (M4).</summary>
public enum Terrain : byte
{
    Clear = 0,

    /// <summary>Impassable and unbuildable: cliffs, boulders.</summary>
    Rock = 1,
}
