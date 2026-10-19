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
    /// The co-op match for <paramref name="mission"/> with <paramref name="players"/> players, every seat a scripted AI
    /// until changed with <see cref="MatchSpec.WithController"/>.
    /// </summary>
    public static MatchSpec ToMatchSpec(CampaignMission mission, int players)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var nests = mission.Nests < MatchSpec.PurgeMaxNests ? mission.Nests : MatchSpec.PurgeMaxNests;
        var harvesters = mission.BonusHarvesters < MatchSpec.MaxBonusHarvesters ? mission.BonusHarvesters : MatchSpec.MaxBonusHarvesters;
        var spec = MatchSpec.Purge(mission.Seed, players, nests, map: mission.Map, difficulty: mission.Difficulty) with
        {
            NestTier = mission.NestTier,
            BonusCredits = mission.BonusCredits,
            BonusHarvesters = harvesters,
        };
        spec.Validate();
        return spec;
    }

    /// <summary>
    /// How a finished (or abandoned) mission went, from the match's end state alone, so every peer derives the same
    /// result: won if the players purged the Maw; the nests destroyed and those left standing.
    /// </summary>
    public static MissionResult ResultOf(HeadlessMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var simulation = match.Simulation;
        var standing = simulation.World.Store<Nest>().Count;
        var destroyed = match.NestsAtStart > standing ? match.NestsAtStart - standing : 0;
        var won = simulation.IsOver && simulation.Goal == MatchGoal.Purge && simulation.WinningTeam != GameSimulation.MawTeam;
        return new MissionResult(won, destroyed, standing, simulation.Tick);
    }
}
