namespace Ashfall.Content;

/// <summary>
/// Percent of a weapon's base damage dealt to each armour type (GDD section 4). Counters are data:
/// a rifle does 10% to heavy armour, an anti-armour rocket 100%.
/// </summary>
public static class DamageTable
{
    // Rows: warhead. Columns: None (infantry), Light, Heavy, Structure, Air, Biomass, Carapace.
    // No single warhead answers the Maw: flame shreds Biomass but barely marks Carapace; anti-armour the reverse.
    private static readonly int[,] Percentages =
    {
        { 100, 50, 10, 10, 40, 90, 10 },    // SmallArms
        { 25, 100, 100, 60, 0, 20, 100 },   // AntiArmour
        { 120, 75, 50, 100, 0, 110, 60 },   // HighExplosive
        { 150, 60, 20, 50, 0, 175, 25 },    // Flame
        { 30, 20, 10, 0, 120, 40, 10 },     // Flak
        { 70, 90, 90, 80, 60, 70, 80 },     // Energy
        { 80, 70, 50, 150, 30, 0, 0 },      // Acid (Maw)
        { 120, 80, 40, 60, 0, 0, 0 },       // Rending (Maw)
    };

    public static int Percent(Warhead warhead, ArmorType armor) => Percentages[(int)warhead, (int)armor];

    /// <summary>Damage after armour, in whole hit points, truncated. Zero means the weapon cannot harm the target.</summary>
    public static int Apply(int baseDamage, Warhead warhead, ArmorType armor) =>
        baseDamage * Percent(warhead, armor) / 100;
}
