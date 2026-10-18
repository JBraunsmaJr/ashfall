using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>
/// The Maw's M5 roster (GDD section 3). Creatures: the Skitter (chaff, in hundreds), the Spitter (a specialist whose
/// acid arcs over walls onto the buildings behind them) and the Siegebeast (a slow behemoth that breaks walls). Nests
/// evolve as they feed: Spawning Pit (Skitters), Brood Nest (adds Spitters), Behemoth Den (adds Siegebeasts).
/// Numbers are starting values for balancing, not final.
/// </summary>
public static class MawContent
{
    public const string Skitter = "maw.skitter";

    public const string Spitter = "maw.spitter";

    public const string Siegebeast = "maw.siegebeast";

    public const string SpawningPit = "maw.spawning_pit";

    public const string BroodNest = "maw.brood_nest";

    public const string BehemothDen = "maw.behemoth_den";

    public static IReadOnlyList<CreatureDefinition> Creatures()
    {
        var claws = new WeaponDefinition("maw.skitter_claws", Warhead.Rending, Damage: 8, Range: Fix64.One, CooldownTicks: 12);

        // Outranges riflemen and Pyros by a cell: it has to be answered with rockets, tanks or a sortie.
        var acid = new WeaponDefinition("maw.spitter_acid", Warhead.Acid, Damage: 12, Range: Fix64.FromInt(5), CooldownTicks: 30);

        // High explosive does full damage to structures: a wall segment lasts five blows.
        var ram = new WeaponDefinition("maw.siegebeast_ram", Warhead.HighExplosive, Damage: 80, Range: Fix64.FromRatio(3, 2), CooldownTicks: 30);

        // Appended in this order: creature indices are part of the replay format.
        return
        [
            // 2.4 cells per second: twice as fast as Bulwark infantry, so a wave closes before it can be kited.
            new CreatureDefinition(Skitter, "Skitter", Health: 40, ArmorType.Biomass, Fix64.FromRatio(4, 25), claws,
                AggroRange: Fix64.FromInt(4), BiomassCost: 10),

            // 1.8 cells per second; spits at units that come near and at buildings it can reach over the wall.
            new CreatureDefinition(Spitter, "Spitter", Health: 70, ArmorType.Biomass, Fix64.FromRatio(3, 25), acid,
                AggroRange: Fix64.FromInt(6), BiomassCost: 25),

            // 0.6 cells per second and a whole cell to itself: the swarm piles up behind it. Only anti-armour hurts it.
            new CreatureDefinition(Siegebeast, "Siegebeast", Health: 1_500, ArmorType.Carapace, Fix64.FromRatio(1, 25), ram,
                AggroRange: Fix64.FromInt(2), BiomassCost: 150, CellWeight: 8),
        ];
    }

    /// <summary>The Maw's structures: its nests, one per tier, all 3x3. Never in a player faction's roster.</summary>
    public static IReadOnlyList<StructureDefinition> Structures() =>
    [
        // Carapace-armoured: rifles barely scratch them; it takes anti-armour or artillery to break one.
        new StructureDefinition(SpawningPit, "Spawning Pit", StructureRole.Nest, Cost: 0, BuildTicks: 1, Health: 2_500,
            Width: 3, Height: 3, Power: 0, Prerequisites: [], Sight: 0, Armor: ArmorType.Carapace),
        new StructureDefinition(BroodNest, "Brood Nest", StructureRole.Nest, Cost: 0, BuildTicks: 1, Health: 4_000,
            Width: 3, Height: 3, Power: 0, Prerequisites: [], Sight: 0, Armor: ArmorType.Carapace),
        new StructureDefinition(BehemothDen, "Behemoth Den", StructureRole.Nest, Cost: 0, BuildTicks: 1, Health: 6_000,
            Width: 3, Height: 3, Power: 0, Prerequisites: [], Sight: 0, Armor: ArmorType.Carapace),
    ];

    public static IReadOnlyList<NestDefinition> Nests() =>
    [
        // Fed: 20 Cindrite a second becomes two Skitters a second. Two minutes of feeding and it becomes a Brood Nest,
        // so a pit left alone is a bigger problem later: kill it or starve it early.
        new NestDefinition(SpawningPit, [new SpawnWeight(Skitter, 1)], FeedRadius: 6, FeedIntervalTicks: 15, FeedAmount: 20,
            MaxBiomass: 300, StartingBiomass: 100, SpawnIntervalTicks: 8, EvolvesTo: BroodNest, EvolveAfter: 2_400),

        // About one Spitter in seven. Three and a half more minutes of feeding makes a Behemoth Den.
        new NestDefinition(BroodNest, [new SpawnWeight(Skitter, 6), new SpawnWeight(Spitter, 1)], FeedRadius: 7, FeedIntervalTicks: 15,
            FeedAmount: 30, MaxBiomass: 500, StartingBiomass: 0, SpawnIntervalTicks: 8, EvolvesTo: BehemothDen, EvolveAfter: 6_000),

        new NestDefinition(BehemothDen, [new SpawnWeight(Skitter, 10), new SpawnWeight(Spitter, 3), new SpawnWeight(Siegebeast, 1)],
            FeedRadius: 8, FeedIntervalTicks: 15, FeedAmount: 40, MaxBiomass: 800, StartingBiomass: 0, SpawnIntervalTicks: 8),
    ];

    /// <summary><paramref name="content"/> with the Maw's creatures, nests and nest structures added.</summary>
    public static GameContent AddTo(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new GameContent(content.Units, [.. content.Structures, .. Structures()], content.Factions, content.Economy, Creatures(), Nests());
    }
}
