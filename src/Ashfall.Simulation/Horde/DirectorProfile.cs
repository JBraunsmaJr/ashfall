namespace Ashfall.Simulation.Horde;

/// <summary>
/// How hard the director pushes at one <see cref="MawDifficulty"/> (GDD section 3). Whole numbers only: the profile
/// feeds the simulation.
/// </summary>
/// <param name="BudgetPercent">The brood each assault may grow to, as a percentage of the match's wave size.</param>
/// <param name="TelegraphTicks">Warning before an assault; probes and raids get half.</param>
/// <param name="CooldownTicks">Breathing room after an assault before the next may be called.</param>
/// <param name="HarassIntervalTicks">Ticks between probes and raids while the brood gathers for the next assault.</param>
/// <param name="Raids">Whether the director sends raids at the players' refineries and harvesters.</param>
/// <param name="SplitAssaults">Whether a big assault splits to hit two players at once.</param>
public sealed record DirectorProfile(
    int BudgetPercent,
    int TelegraphTicks,
    int CooldownTicks,
    int HarassIntervalTicks,
    bool Raids,
    bool SplitAssaults)
{
    /// <summary>Starting values, from the GDD's table: 30/22/18/15 s of warning, 0.6x to 2x the budget.</summary>
    public static DirectorProfile For(MawDifficulty difficulty) => difficulty switch
    {
        MawDifficulty.Recruit => new(BudgetPercent: 60, TelegraphTicks: 450, CooldownTicks: 1_350, HarassIntervalTicks: 900, Raids: false, SplitAssaults: false),
        MawDifficulty.Veteran => new(BudgetPercent: 100, TelegraphTicks: 330, CooldownTicks: 1_050, HarassIntervalTicks: 600, Raids: true, SplitAssaults: true),
        MawDifficulty.Elite => new(BudgetPercent: 140, TelegraphTicks: 270, CooldownTicks: 900, HarassIntervalTicks: 450, Raids: true, SplitAssaults: true),
        MawDifficulty.Extinction => new(BudgetPercent: 200, TelegraphTicks: 225, CooldownTicks: 750, HarassIntervalTicks: 300, Raids: true, SplitAssaults: true),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null),
    };
}
