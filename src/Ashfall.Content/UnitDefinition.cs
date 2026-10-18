using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>A unit type.</summary>
/// <param name="SpeedPerTick">Cells moved per tick (15 ticks per second).</param>
/// <param name="AcquisitionRange">How far an idle unit looks for enemies to engage, in cells.</param>
/// <param name="ProducedBy">The structure role that trains this unit.</param>
/// <param name="HarvestCapacity">Cindrite a harvester carries per trip; 0 for units that cannot harvest.</param>
public sealed record UnitDefinition(
    string Id,
    string Name,
    int Cost,
    int BuildTicks,
    int Health,
    ArmorType Armor,
    Fix64 SpeedPerTick,
    WeaponDefinition? Weapon,
    Fix64 AcquisitionRange,
    StructureRole ProducedBy,
    int HarvestCapacity = 0)
{
    public bool IsHarvester => HarvestCapacity > 0;

    public bool IsCombatUnit => Weapon is not null;
}
