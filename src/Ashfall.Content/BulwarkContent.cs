using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>
/// The Bulwark Directorate's M1 roster: enough to play a full match (economy, infantry, armour).
/// Numbers are starting values for balancing, not final. Speeds are cells per tick (15 ticks per second).
/// </summary>
public static class BulwarkContent
{
    public const string FactionId = "bulwark";

    public const string ConstructionYard = "bulwark.construction_yard";
    public const string PowerPlant = "bulwark.power_plant";
    public const string Refinery = "bulwark.refinery";
    public const string Barracks = "bulwark.barracks";
    public const string Factory = "bulwark.factory";

    public const string Rifleman = "bulwark.rifleman";
    public const string Rocketeer = "bulwark.rocketeer";
    public const string Warden = "bulwark.warden";
    public const string Harvester = "bulwark.harvester";

    public static GameContent Create()
    {
        var rifle = new WeaponDefinition("bulwark.rifle", Warhead.SmallArms, Damage: 15, Range: Fix64.FromInt(4), CooldownTicks: 15);
        var rocket = new WeaponDefinition("bulwark.rocket", Warhead.AntiArmour, Damage: 45, Range: Fix64.FromInt(5), CooldownTicks: 45);
        var cannon = new WeaponDefinition("bulwark.cannon", Warhead.AntiArmour, Damage: 40, Range: Fix64.FromInt(5), CooldownTicks: 30);

        // 1.2 cells per second for infantry and harvesters, 1.8 for the Warden tank.
        var infantrySpeed = Fix64.FromRatio(2, 25);
        var tankSpeed = Fix64.FromRatio(3, 25);

        UnitDefinition[] units =
        [
            new(Rifleman, "Rifleman", Cost: 150, BuildTicks: 90, Health: 125, ArmorType.None, infantrySpeed, rifle,
                AcquisitionRange: Fix64.FromInt(6), StructureRole.Barracks, Sight: 6),
            new(Rocketeer, "Rocketeer", Cost: 300, BuildTicks: 120, Health: 100, ArmorType.None, infantrySpeed, rocket,
                AcquisitionRange: Fix64.FromInt(7), StructureRole.Barracks, Sight: 7),
            new(Warden, "Warden light tank", Cost: 700, BuildTicks: 150, Health: 450, ArmorType.Light, tankSpeed, cannon,
                AcquisitionRange: Fix64.FromInt(7), StructureRole.Factory, Sight: 7),
            new(Harvester, "Harvester", Cost: 1_400, BuildTicks: 210, Health: 700, ArmorType.Heavy, infantrySpeed, Weapon: null,
                AcquisitionRange: Fix64.Zero, StructureRole.Factory, HarvestCapacity: 700, Sight: 4),
        ];

        StructureDefinition[] structures =
        [
            new(ConstructionYard, "Construction Yard", StructureRole.ConstructionYard, Cost: 5_000, BuildTicks: 600, Health: 1_500,
                Width: 3, Height: 3, Power: 0, Prerequisites: [], Sight: 6),
            new(PowerPlant, "Power Plant", StructureRole.PowerPlant, Cost: 300, BuildTicks: 150, Health: 500,
                Width: 2, Height: 2, Power: 100, Prerequisites: []),
            new(Refinery, "Refinery", StructureRole.Refinery, Cost: 2_000, BuildTicks: 300, Health: 900,
                Width: 3, Height: 3, Power: -30, Prerequisites: [StructureRole.PowerPlant], FreeUnitId: Harvester),
            new(Barracks, "Barracks", StructureRole.Barracks, Cost: 300, BuildTicks: 150, Health: 600,
                Width: 2, Height: 2, Power: -20, Prerequisites: [StructureRole.PowerPlant]),
            new(Factory, "War Factory", StructureRole.Factory, Cost: 2_000, BuildTicks: 300, Health: 1_000,
                Width: 3, Height: 3, Power: -40, Prerequisites: [StructureRole.Refinery]),
        ];

        FactionDefinition[] factions =
        [
            new(FactionId, "Bulwark Directorate", ConstructionYard,
                StructureIds: [ConstructionYard, PowerPlant, Refinery, Barracks, Factory],
                UnitIds: [Rifleman, Rocketeer, Warden, Harvester]),
        ];

        return new GameContent(units, structures, factions, new EconomyRules());
    }
}
