using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>
/// The Bulwark Directorate's roster: the M1 core (economy, infantry, armour) plus the M5 answers to the Maw, the
/// Pyro's area flame and the Wall.
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
    public const string Wall = "bulwark.wall";
    public const string RepairBay = "bulwark.repair_bay";
    public const string GuardTower = "bulwark.guard_tower";
    public const string Bunker = "bulwark.bunker";

    public const string Rifleman = "bulwark.rifleman";
    public const string Rocketeer = "bulwark.rocketeer";
    public const string Warden = "bulwark.warden";
    public const string Harvester = "bulwark.harvester";
    public const string Pyro = "bulwark.pyro";
    public const string Medic = "bulwark.medic";

    public static GameContent Create()
    {
        var rifle = new WeaponDefinition("bulwark.rifle", Warhead.SmallArms, Damage: 15, Range: Fix64.FromInt(4), CooldownTicks: 15);
        var rocket = new WeaponDefinition("bulwark.rocket", Warhead.AntiArmour, Damage: 45, Range: Fix64.FromInt(5), CooldownTicks: 45);
        var cannon = new WeaponDefinition("bulwark.cannon", Warhead.AntiArmour, Damage: 40, Range: Fix64.FromInt(5), CooldownTicks: 30);

        // The guard tower's twin machine guns: chaff-shredding, outranging every creature but the Spitter.
        var towerGuns = new WeaponDefinition("bulwark.tower_guns", Warhead.SmallArms, Damage: 14, Range: Fix64.FromInt(6), CooldownTicks: 8);

        // Short, fast and wide: each burst scorches everything within a cell of its target. The answer to chaff.
        var flamer = new WeaponDefinition("bulwark.flamer", Warhead.Flame, Damage: 10, Range: Fix64.FromInt(3), CooldownTicks: 9,
            SplashRadius: Fix64.One);

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

            // Appended: unit indices are part of the replay format.
            new(Pyro, "Pyro", Cost: 250, BuildTicks: 120, Health: 110, ArmorType.None, infantrySpeed, flamer,
                AcquisitionRange: Fix64.FromInt(5), StructureRole.Barracks, Sight: 5),

            // Unarmed: keeps the infantry around it standing (12 hit points a second to the worst hurt within 4 cells).
            new(Medic, "Medic", Cost: 200, BuildTicks: 90, Health: 90, ArmorType.None, infantrySpeed, Weapon: null,
                AcquisitionRange: Fix64.FromInt(4), StructureRole.Barracks, Sight: 5, HealPerPulse: 12),
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

            // Appended. Cheap and quick so a line goes up between waves; the swarm has to chew through it.
            new(Wall, "Wall", StructureRole.Wall, Cost: 50, BuildTicks: 15, Health: 400,
                Width: 1, Height: 1, Power: 0, Prerequisites: [StructureRole.Barracks], Sight: 0),

            // Vehicles parked beside it are mended for credits.
            new(RepairBay, "Repair Bay", StructureRole.RepairBay, Cost: 800, BuildTicks: 240, Health: 800,
                Width: 3, Height: 3, Power: -30, Prerequisites: [StructureRole.Factory]),

            // Defences. A tower fires on its own; a bunker shelters four infantry, who fire out a cell further than
            // they could standing in the open and cannot be hit until it falls.
            new(GuardTower, "Guard Tower", StructureRole.Defence, Cost: 500, BuildTicks: 180, Health: 900,
                Width: 1, Height: 1, Power: -10, Prerequisites: [StructureRole.Barracks], Sight: 7, Weapon: towerGuns),
            new(Bunker, "Bunker", StructureRole.Defence, Cost: 400, BuildTicks: 150, Health: 1_400,
                Width: 2, Height: 2, Power: 0, Prerequisites: [StructureRole.Barracks], Sight: 6, Garrison: 4),
        ];

        FactionDefinition[] factions =
        [
            new(FactionId, "Bulwark Directorate", ConstructionYard,
                StructureIds: [ConstructionYard, PowerPlant, Refinery, Barracks, Factory, Wall, RepairBay, GuardTower, Bunker],
                UnitIds: [Rifleman, Rocketeer, Warden, Harvester, Pyro, Medic]),
        ];

        return new GameContent(units, structures, factions, new EconomyRules());
    }
}
