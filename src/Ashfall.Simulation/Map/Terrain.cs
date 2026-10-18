namespace Ashfall.Simulation.Map;

/// <summary>
/// Ground type of a cell. Ground units walk on Clear, Ramp and Bridge; only Clear takes buildings.
/// Height is separate (<see cref="GridMap.HeightAt"/>): cliffs between height levels are Rock cells.
/// </summary>
public enum Terrain : byte
{
    Clear = 0,

    /// <summary>Impassable and unbuildable: cliffs, boulders, rock outcrops.</summary>
    Rock = 1,

    /// <summary>Impassable to ground units, unbuildable.</summary>
    Water = 2,

    /// <summary>Walkable but unbuildable: a crossing over water.</summary>
    Bridge = 3,

    /// <summary>Walkable but unbuildable: a slope joining two height levels.</summary>
    Ramp = 4,

    /// <summary>Impassable and unbuildable woodland.</summary>
    Trees = 5,
}
