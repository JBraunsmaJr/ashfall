﻿using Ashfall.Campaign;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Players;

namespace Ashfall.Campaign.Tests;

/// <summary>Progress you can see (GDD section 7): samples and research, veteran detachments, commanders and perks.</summary>
public class ProgressionTests
{
    private static CampaignState New(uint seed = 7) => CampaignState.Start(seed, CampaignRuleSets.Standard, MawDifficulty.Veteran);

    private static CampaignTarget Front(CampaignState campaign) => campaign.Targets.First(target => !target.IsEvent);

    [Fact]
    public void Missions_earn_samples_and_research_spends_them_for_everyone()
    {
        var rules = CampaignRuleSets.Standard;
        Assert.Equal((4 * 3) + 5, rules.SamplesFor(true, 4, 2));
        Assert.Equal(2 * 2, rules.SamplesFor(false, 2, 1));

        var campaign = New();
        Assert.Equal(0, campaign.Samples);
        Assert.False(campaign.CanResearch("weapons-1", out var reason));
        Assert.Contains("samples", reason, StringComparison.Ordinal);
        campaign.Play(new TurnRecord(Front(campaign), campaign.Difficulty, MissionResult.Victory(3, samples: 80)));
        Assert.True(campaign.Samples >= 80);

        var before = campaign.Samples;
        Assert.False(campaign.CanResearch("weapons-2", out reason));
        Assert.Contains("Hardened Rounds", reason, StringComparison.Ordinal);
        campaign.Research("weapons-1");
        Assert.Equal(before - 20, campaign.Samples);
        Assert.True(campaign.IsResearched("weapons-1"));
        Assert.False(campaign.CanResearch("weapons-1", out _));
        campaign.Research("logistics-1");
        campaign.Research("intel-1");
        Assert.Throws<InvalidOperationException>(() => campaign.Research("no-such-thing"));

        var mission = campaign.PlanMission(Front(campaign), campaign.Difficulty, seats: 3);
        Assert.Equal(3, mission.Loadouts.Count);
        Assert.All(mission.Loadouts, loadout =>
        {
            Assert.Equal(110, loadout.Modifiers.DamagePercent);
            Assert.Equal(750, loadout.Credits);
        });
        Assert.True(mission.RevealNests);
        Assert.Equal(0, mission.TelegraphBonusTicks);
        Assert.Equal([(1, "weapons-1"), (1, "logistics-1"), (1, "intel-1")], campaign.ResearchLog);
    }

    [Fact]
    public void Survivors_become_each_seats_detachment_and_the_fallen_are_gone()
    {
        var campaign = New();
        IReadOnlyList<IReadOnlyList<string>> survivors = [["bulwark.warden", "bulwark.rifleman"], ["bulwark.rocketeer"]];
        campaign.Play(new TurnRecord(Front(campaign), campaign.Difficulty, new MissionResult(false, 1, 2, 9_000, 2, survivors)));
        Assert.Equal(["bulwark.warden", "bulwark.rifleman"], campaign.DetachmentOf(0));
        Assert.Equal(["bulwark.rocketeer"], campaign.DetachmentOf(1));
        Assert.Empty(campaign.DetachmentOf(2));

        var mission = campaign.PlanMission(Front(campaign), campaign.Difficulty, seats: 2);
        Assert.Equal(["bulwark.warden", "bulwark.rifleman"], mission.Loadouts[0].Units);
        Assert.Equal(["bulwark.rocketeer"], mission.Loadouts[1].Units);
        Assert.Equal([campaign.Rules.DetachmentSize, campaign.Rules.DetachmentSize], mission.DetachmentSizes);

        // Seat 0 lost everything in the next mission; seat 1's result is silent, so its detachment stays.
        campaign.Play(new TurnRecord(Front(campaign), campaign.Difficulty, new MissionResult(false, 0, 2, 9_000, 0, [[]])));
        Assert.Empty(campaign.DetachmentOf(0));
        Assert.Equal(["bulwark.rocketeer"], campaign.DetachmentOf(1));
    }

    [Fact]
    public void Research_detachments_and_samples_survive_a_save_and_old_saves_still_load()
    {
        var campaign = New(12);
        campaign.Play(new TurnRecord(Front(campaign), campaign.Difficulty, new MissionResult(true, 3, 0, 20_000, 40, [["bulwark.warden"], []])));
        campaign.Research("armour-1");
        campaign.Research("harvest-1");
        campaign.Play(new TurnRecord(Front(campaign), campaign.Difficulty, MissionResult.Defeat(1, 2, 30_000, 2)));
        var text = CampaignSave.Write(campaign);
        Assert.Contains("research armour-1\nresearch harvest-1\nturn ", text, StringComparison.Ordinal);
        Assert.Contains(" samples=40 keep0=bulwark.warden keep1=", text, StringComparison.Ordinal);

        var loaded = CampaignSave.Read(text);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(campaign.ComputeHash(), loaded.Campaign.ComputeHash());
        Assert.Equal(campaign.Samples, loaded.Campaign.Samples);
        Assert.Equal(campaign.ResearchLog, loaded.Campaign.ResearchLog);
        Assert.Equal(["bulwark.warden"], loaded.Campaign.DetachmentOf(0));
        Assert.Equal(text, CampaignSave.Write(loaded.Campaign));

        // A format-1 save (no samples, no detachments) reads as a campaign without them.
        var old = CampaignState.Start(12, CampaignRuleSets.Standard, MawDifficulty.Veteran);
        var target = Front(old);
        var v1 = $"ashfall-campaign 1\ncode {old.Code}\nrules standard {Ashfall.Core.StateHasher.Format(old.RulesHash)}\nturn region {target.Id} Veteran won 3 0 20000\n";
        var reread = CampaignSave.Read(v1).Campaign;
        Assert.Equal(1, reread.Turn);
        Assert.Equal(0, reread.Samples);

        // Research a save could never have afforded is refused.
        Assert.Throws<FormatException>(() => CampaignSave.Read(v1.Replace("turn region", "research weapons-2\nturn region", StringComparison.Ordinal)));
    }

    [Fact]
    public void Commanders_level_up_unlock_perks_and_lend_their_bonuses_to_their_seat()
    {
        var profile = new CommanderProfile();
        var reyes = Commanders.Find("reyes")!;
        profile.Lead(reyes);
        Assert.Equal(1, profile.LevelOf(reyes));
        Assert.Equal([reyes.Perks[0]], profile.ActivePerks); // unlocked perks are on until the player chooses
        Assert.True(profile.Toggle(reyes.Perks[0]));
        Assert.Empty(profile.ActivePerks);
        Assert.True(profile.Toggle(reyes.Perks[0]));
        Assert.False(profile.Toggle(reyes.Perks[1])); // locked at level 1
        Assert.Equal(105, profile.Bonus().Modifiers.DamagePercent);

        Assert.Equal(140, Commanders.ExperienceFor(MissionResult.Victory(4)));
        Assert.Equal(40, Commanders.ExperienceFor(MissionResult.Defeat(0, 3)));
        Assert.Equal(3, profile.AddExperience(260)); // levels 2 and 3: their perks switch on while there is room
        Assert.Equal(3, profile.ActivePerks.Count);
        Assert.Null(profile.AddExperience(10));
        profile.AddExperience(5_000);
        Assert.Equal(Commanders.MaxActivePerks, profile.ActivePerks.Count);
        Assert.False(profile.Toggle(reyes.Perks[^1]) && profile.ActivePerks.Count > Commanders.MaxActivePerks);

        var copy = CommanderProfile.Parse(profile.Serialize());
        Assert.Equal(reyes, copy.Active);
        Assert.Equal(profile.ExperienceOf(reyes), copy.ExperienceOf(reyes));
        Assert.Equal(profile.ActivePerks, copy.ActivePerks);
        Assert.Equal(Commanders.All[0], CommanderProfile.Parse("commander=nobody\nxp.reyes=abc\n").Active);

        // The commander's bonuses ride on its own seat only.
        var campaign = New();
        var mission = campaign.PlanMission(Front(campaign), campaign.Difficulty, seats: 2, commanders: [profile.Bonus()]);
        Assert.Equal(profile.Bonus().Modifiers.DamagePercent, mission.Loadouts[0].Modifiers.DamagePercent);
        Assert.Equal(PlayerModifiers.None, mission.Loadouts[1].Modifiers);
        Assert.Equal(campaign.Rules.DetachmentSize + profile.Bonus().DetachmentSlots, mission.DetachmentSizes[0]);
    }
}
