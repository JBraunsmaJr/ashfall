using Ashfall.Campaign;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;

namespace Ashfall.Session.Campaigns;

/// <summary>
/// The bridge between the campaign layer and matches (GDD section 12: the campaign turns region data into a match spec
/// and reads the result back). Holdout, hive assault and escort missions are played as Purges until those modes exist.
/// </summary>
public static class CampaignMatches
{
    /// <summary>
    /// The co-op match for <paramref name="mission"/>: one player per seat it was planned for, with each seat's
    /// loadout, every seat a scripted AI until changed with <see cref="MatchSpec.WithController"/>.
    /// </summary>
    public static MatchSpec ToMatchSpec(CampaignMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var nests = mission.Nests < MatchSpec.PurgeMaxNests ? mission.Nests : MatchSpec.PurgeMaxNests;
        var harvesters = mission.BonusHarvesters < MatchSpec.MaxBonusHarvesters ? mission.BonusHarvesters : MatchSpec.MaxBonusHarvesters;
        var spec = MatchSpec.Purge(mission.Seed, mission.Loadouts.Count, nests, map: mission.Map, difficulty: mission.Difficulty) with
        {
            NestTier = mission.NestTier,
            BonusCredits = mission.BonusCredits,
            BonusHarvesters = harvesters,
            Loadouts = mission.Loadouts,
            RevealNests = mission.RevealNests,
            TelegraphBonusTicks = mission.TelegraphBonusTicks,
        };
        spec.Validate();
        return spec;
    }

    /// <summary>
    /// How a finished (or abandoned) mission went, from the match's end state alone, so every peer derives the same
    /// result: won if the players purged the Maw; the nests destroyed and those left standing; the samples recovered;
    /// and, per seat, the surviving units kept for the next mission (the most valuable combat units first, as many as
    /// the seat may keep).
    /// </summary>
    /// <param name="rules">The campaign's rules, for samples and detachment sizes (standard if null).</param>
    public static MissionResult ResultOf(HeadlessMatch match, CampaignMission? mission = null, CampaignRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        var simulation = match.Simulation;
        var world = simulation.World;
        var standing = world.Store<Nest>().Count;
        var destroyed = match.NestsAtStart > standing ? match.NestsAtStart - standing : 0;
        var won = simulation.IsOver && simulation.Goal == MatchGoal.Purge && simulation.WinningTeam != GameSimulation.MawTeam;
        if (mission is null)
        {
            return new MissionResult(won, destroyed, standing, simulation.Tick);
        }

        rules ??= CampaignRuleSets.Standard;
        var survivors = new IReadOnlyList<string>[simulation.Players.Count];
        for (var seat = 0; seat < survivors.Length; seat++)
        {
            var keep = seat < mission.DetachmentSizes.Count ? mission.DetachmentSizes[seat] : rules.DetachmentSize;
            survivors[seat] = world.Store<UnitInfo>().EntityIndices.ToArray()
                .Where(index => world.Store<Owner>().Get(index).PlayerId == seat && !world.Store<Health>().Get(index).IsDead)
                .Select(index => (Index: index, Definition: simulation.Content.Unit(world.Store<UnitInfo>().Get(index).UnitType)))
                .Where(unit => unit.Definition.Weapon is not null && !unit.Definition.IsHarvester)
                .OrderByDescending(unit => unit.Definition.Cost)
                .ThenBy(unit => unit.Definition.Id, StringComparer.Ordinal)
                .ThenBy(unit => unit.Index)
                .Take(keep)
                .Select(unit => unit.Definition.Id)
                .ToArray();
        }

        return new MissionResult(won, destroyed, standing, simulation.Tick, rules.SamplesFor(won, destroyed, mission.NestTier), survivors);
    }
}
