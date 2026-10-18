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
}
