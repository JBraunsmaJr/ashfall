namespace Ashfall.Content;

/// <summary>What a weapon's damage is good against. Values are part of the replay format: append only.</summary>
public enum Warhead : byte
{
    SmallArms = 0,
    AntiArmour = 1,
    HighExplosive = 2,
    Flame = 3,
    Flak = 4,
    Energy = 5,

    /// <summary>Maw spit: eats through structures, useless against other creatures.</summary>
    Acid = 6,

    /// <summary>Maw claws and jaws: tears infantry apart, weak against armour.</summary>
    Rending = 7,
}
