namespace Ashfall.Simulation;

/// <summary>Why a structure can or cannot be placed.</summary>
public enum PlacementResult
{
    Ok,
    NotAPlayer,
    UnknownStructure,

    /// <summary>Not in the player's faction, or a type that can only be placed at setup (the Construction Yard).</summary>
    NotBuildable,
    NoConstructionYard,

    /// <summary>The player is already constructing something; one structure at a time.</summary>
    AlreadyBuilding,
    MissingPrerequisite,
    CannotAfford,

    /// <summary>Off the map, on rock, Cindrite, another structure or a unit.</summary>
    Blocked,
    TooFarFromBase,
}
