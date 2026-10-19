using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>A unit type.</summary>
/// <param name="SpeedPerTick">Cells moved per tick (15 ticks per second).</param>
/// <param name="AcquisitionRange">How far an idle unit looks for enemies to engage, in cells.</param>
/// <param name="ProducedBy">The structure role that trains this unit.</param>
/// <param name="HarvestCapacity">Cindrite a harvester carries per trip; 0 for units that cannot harvest.</param>
/// <param name="Sight">How far the unit reveals the fog of war, in cells.</param>
/// <param name="HealPerPulse">
/// For a medic: hit points it restores to the most wounded friendly infantry within <paramref name="AcquisitionRange"/>
/// every <see cref="EconomyRules.RepairIntervalTicks"/>; 0 for everyone else.
/// </param>
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
    int HarvestCapacity = 0,
    int Sight = 6,
    int HealPerPulse = 0)
{
    public bool IsHarvester => HarvestCapacity > 0;

    public bool IsMedic => HealPerPulse > 0;

    /// <summary>Infantry (trained at a barracks): what a medic heals.</summary>
    public bool IsInfantry => ProducedBy == StructureRole.Barracks;

    /// <summary>Vehicles (built at a war factory): what a repair bay mends.</summary>
    public bool IsVehicle => ProducedBy == StructureRole.Factory;

    public bool IsCombatUnit => Weapon is not null;
}
