using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>
/// The Maw's M5 roster (GDD section 3): the Skitter, the chaff that arrives in hundreds, and the Spawning Pit, the
/// first nest tier, which feeds on Cindrite and breeds Skitters. Specialists, behemoths and the higher nest tiers
/// follow. Numbers are starting values for balancing, not final.
/// </summary>
public static class MawContent
{
    public const string Skitter = "maw.skitter";

    public const string SpawningPit = "maw.spawning_pit";

    public static IReadOnlyList<CreatureDefinition> Creatures()
    {
        var claws = new WeaponDefinition("maw.skitter_claws", Warhead.Rending, Damage: 8, Range: Fix64.One, CooldownTicks: 12);

        return
        [
            // 2.4 cells per second: twice as fast as Bulwark infantry, so a wave closes before it can be kited.
            new CreatureDefinition(Skitter, "Skitter", Health: 40, ArmorType.Biomass, Fix64.FromRatio(4, 25), claws,
                AggroRange: Fix64.FromInt(4), BiomassCost: 10),
        ];
    }

    /// <summary>The Maw's structures: its nests. Never in a player faction's roster.</summary>
    public static IReadOnlyList<StructureDefinition> Structures() =>
    [
        // Carapace-armoured: rifles barely scratch it; it takes anti-armour or artillery to break.
        new StructureDefinition(SpawningPit, "Spawning Pit", StructureRole.Nest, Cost: 0, BuildTicks: 1, Health: 2_500,
            Width: 3, Height: 3, Power: 0, Prerequisites: [], Sight: 0, Armor: ArmorType.Carapace),
    ];

    public static IReadOnlyList<NestDefinition> Nests() =>
    [
        // Fed: 20 Cindrite a second becomes two Skitters a second. A 5x5 field of 400 lasts about eight minutes
        // without regrowth; harvesters (about 75 a second each) starve it far faster.
        new NestDefinition(SpawningPit, Skitter, FeedRadius: 6, FeedIntervalTicks: 15, FeedAmount: 20, MaxBiomass: 300,
            StartingBiomass: 100, SpawnIntervalTicks: 8),
    ];

    /// <summary><paramref name="content"/> with the Maw's creatures, nests and nest structures added.</summary>
    public static GameContent AddTo(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new GameContent(content.Units, [.. content.Structures, .. Structures()], content.Factions, content.Economy, Creatures(), Nests());
    }
}
