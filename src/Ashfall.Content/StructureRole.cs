namespace Ashfall.Content;

/// <summary>What a structure does for its owner. The simulation keys behaviour off the role, not the name.</summary>
public enum StructureRole : byte
{
    ConstructionYard = 0,
    PowerPlant = 1,
    Refinery = 2,
    Barracks = 3,
    Factory = 4,

    /// <summary>A Maw nest: never built by a player; it feeds on Cindrite and spawns creatures (GDD section 3).</summary>
    Nest = 5,

    /// <summary>
    /// A wall segment: blocks movement and soaks the swarm's attacks. Walls do not keep a player in the match and do
    /// not extend where the player may build.
    /// </summary>
    Wall = 6,

    /// <summary>Mends the owner's vehicles parked beside it, for credits.</summary>
    RepairBay = 7,

    /// <summary>A guard tower or bunker: holds ground for its owner. Like walls, defences do not extend the build radius.</summary>
    Defence = 8,
}
