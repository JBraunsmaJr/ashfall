using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>
/// The Maw's M5 roster (GDD section 3). The swarm spike starts with the Skitter, the chaff that arrives
/// in hundreds; specialists and behemoths follow. Numbers are starting values for balancing, not final.
/// </summary>
public static class MawContent
{
    public const string Skitter = "maw.skitter";

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
}
