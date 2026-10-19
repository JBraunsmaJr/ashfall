﻿using Ashfall.Campaign;
using Ashfall.Simulation.Horde;

namespace Ashfall.Session.Campaigns;

/// <summary>One campaign mission played out without a window.</summary>
/// <param name="Mission">The mission as planned from the region.</param>
/// <param name="Spec">The match it became.</param>
/// <param name="Result">How it went, read from the match's end state.</param>
/// <param name="Report">What the turn did to the world.</param>
public sealed record PlayedMission(CampaignMission Mission, MatchSpec Spec, MissionResult Result, TurnReport Report);

/// <summary>
/// Plays campaign missions headless with every seat a scripted AI: the campaign's target becomes a real Purge, the match
/// runs to its end (or the tick limit, which counts as a loss), and the result advances the campaign. Used by the
/// headless runner's campaign mode and the campaign end-to-end tests.
/// </summary>
public static class CampaignRunner
{
    /// <summary>
    /// Plays <paramref name="target"/> as this turn's mission with <paramref name="players"/> scripted AIs (no commander
    /// perks), buying nothing: research is the players' choice.
    /// </summary>
    public static async Task<PlayedMission> PlayTurnAsync(
        CampaignState campaign,
        CampaignTarget target,
        int players,
        long tickLimit = MatchSpec.SkirmishTickLimit,
        MawDifficulty? difficulty = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var played = difficulty ?? campaign.Difficulty;
        var mission = campaign.PlanMission(target, played, players);
        var spec = CampaignMatches.ToMatchSpec(mission);
        await using var match = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled).ConfigureAwait(false);
        await match.RunToCompletionAsync(tickLimit, cancellationToken).ConfigureAwait(false);
        var result = CampaignMatches.ResultOf(match, mission, campaign.Rules);
        var report = campaign.Play(new TurnRecord(target, played, result));
        return new PlayedMission(mission, spec, result, report);
    }
}
