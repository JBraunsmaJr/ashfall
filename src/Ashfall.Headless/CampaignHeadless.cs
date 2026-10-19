﻿using System.Globalization;
using Ashfall.Campaign;
using Ashfall.Core;
using Ashfall.Session;
using Ashfall.Session.Campaigns;

namespace Ashfall.Headless;

/// <summary>
/// <c>--mode campaign</c>: plays campaign missions as real Purges with scripted AIs on every seat, the autopilot choosing
/// each turn's target and spending samples on research, and saves after every turn. Proves the whole loop (region to match to result to world) end to end
/// without a window.
/// </summary>
internal static class CampaignHeadless
{
    public static async Task<int> RunAsync(HeadlessOptions options, CancellationToken cancellationToken)
    {
        CampaignState campaign;
        if (options.CampaignSave is { } path && File.Exists(path))
        {
            if (!CampaignFiles.TryLoad(path, out var loaded, out var error))
            {
                await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
                return 2;
            }

            foreach (var warning in loaded!.Warnings)
            {
                Console.WriteLine("warning: " + warning);
            }

            campaign = loaded.Campaign;
            Console.WriteLine($"Continuing {CampaignFiles.Summary(campaign)} from {path}");
        }
        else
        {
            var code = options.CampaignCode ?? CampaignCode.For((uint)(options.Seed & uint.MaxValue), options.CampaignRules, options.Difficulty);
            campaign = CampaignState.Start(code);
            Console.WriteLine($"New campaign {CampaignFiles.Summary(campaign)}: {campaign.World.Regions.Count} regions in {campaign.World.Theatres.Count} theatres");
        }

        var players = Math.Clamp(options.Players, 1, MatchSpec.PurgeMaxPlayers);
        var played = 0;
        while (campaign.Outcome == CampaignOutcome.InProgress && (options.Missions is not { } limit || played < limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var researched = campaign.ResearchLog.Count;
            CampaignAutopilot.BuyResearch(campaign);
            foreach (var (_, id) in campaign.ResearchLog.Skip(researched))
            {
                Console.WriteLine($"research: {campaign.Rules.FindResearch(id)!.Name}");
            }

            var target = CampaignAutopilot.ChooseTarget(campaign);
            var turn = await CampaignRunner.PlayTurnAsync(campaign, target, players, options.Ticks, cancellationToken: cancellationToken).ConfigureAwait(false);
            played++;
            var mission = turn.Mission;
            var region = campaign.World.Regions[mission.Region];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"turn {campaign.Turn}: {mission.Kind} {region.Name} ({mission.Nests} tier-{mission.NestTier} nests, {mission.Map.ToCode()}) -> {(turn.Result.Won ? "won" : "lost")} in {turn.Result.Ticks / 900.0:F1} min, {turn.Result.NestsDestroyed} destroyed, {turn.Result.NestsStanding} standing, {turn.Result.Samples} samples; meter {campaign.Meter}%, {campaign.HivesLeft} hives left"));
            foreach (var line in turn.Report.Lines)
            {
                Console.WriteLine("  " + line);
            }

            if (options.CampaignSave is { } save)
            {
                CampaignFiles.Save(campaign, save);
            }
        }

        Console.WriteLine($"Campaign {campaign.Outcome}: {CampaignFiles.Summary(campaign)}; state {StateHasher.Format(campaign.ComputeHash())}");
        if (options.HashOut is not null)
        {
            await File.WriteAllTextAsync(options.HashOut, StateHasher.Format(campaign.ComputeHash()) + "\n", cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }
}
