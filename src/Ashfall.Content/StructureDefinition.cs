namespace Ashfall.Content;

/// <summary>A structure type.</summary>
/// <param name="Width">Footprint width in cells.</param>
/// <param name="Height">Footprint height in cells.</param>
/// <param name="Power">Power produced (positive) or consumed (negative) once construction completes.</param>
/// <param name="Prerequisites">Roles the owner must already have completed before this can be placed.</param>
/// <param name="FreeUnitId">A unit delivered when construction completes (the refinery's harvester), or null.</param>
/// <param name="Sight">How far beyond its footprint the structure reveals the fog of war, in cells.</param>
/// <param name="Armor">How the structure resists damage: <see cref="ArmorType.Structure"/> for buildings, Carapace for nests.</param>
/// <param name="Weapon">A turret's gun: it fires at enemies in range on its own, never moving; null for everything else.</param>
/// <param name="Garrison">How many infantry can shelter inside and fire out (a bunker); 0 for everything else.</param>
public sealed record StructureDefinition(
    string Id,
    string Name,
    StructureRole Role,
    int Cost,
    int BuildTicks,
    int Health,
    int Width,
    int Height,
    int Power,
    IReadOnlyList<StructureRole> Prerequisites,
    string? FreeUnitId = null,
    int Sight = 4,
    ArmorType Armor = ArmorType.Structure,
    WeaponDefinition? Weapon = null,
    int Garrison = 0);
