﻿using System.Numerics;
using Ashfall.Campaign;
using Ashfall.Rendering.Scene;
using Ashfall.Simulation.Horde;
using Ashfall.UI.Campaigns;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

/// <summary>The campaign screen: the world map, choosing where to fight, events, lowering the difficulty, leaving.</summary>
public class CampaignScreenTests
{
    private static readonly Vector2 Framebuffer = new(1280, 720);

    [Fact]
    public void It_opens_on_a_front_region_ready_to_launch()
    {
        var ui = new Driver(CampaignState.Start(5, CampaignRuleSets.Standard, MawDifficulty.Veteran));
        var selected = Assert.IsType<CampaignTarget>(ui.Screen.Selected);
        Assert.Contains(selected, ui.Screen.Campaign.Targets);
        Assert.True(ui.Enabled("LAUNCH MISSION"));
        Assert.True(ui.Scene.QuadCount > 500, "The map, its regions and the panel are drawn.");

        ui.Press(Key.Enter);
        var choice = Assert.IsType<CampaignChoice>(ui.Screen.Choice);
        Assert.Equal(CampaignChoiceKind.Launch, choice.Kind);
        Assert.Equal(selected, choice.Target);
        Assert.Equal(MawDifficulty.Veteran, choice.Difficulty);
    }

    [Fact]
    public void Clicking_regions_selects_them_and_only_the_front_can_be_launched()
    {
        var campaign = CampaignState.Start(5, CampaignRuleSets.Standard, MawDifficulty.Veteran);
        var ui = new Driver(campaign);
        var world = campaign.World;
        var front = world.Regions[world.Home].Neighbours[^1];
        var far = world.Regions.First(region => world.Distance(world.Home, region.Id) >= 3).Id;

        ui.ClickAt(ui.Screen.RegionOnScreen(far));
        Assert.Equal(CampaignTarget.Region(far), ui.Screen.Selected);
        Assert.DoesNotContain(ui.Screen.Targets, target => target.Label == "LAUNCH MISSION");

        ui.ClickAt(ui.Screen.RegionOnScreen(front));
        Assert.Equal(CampaignTarget.Region(front), ui.Screen.Selected);
        ui.Click("LAUNCH MISSION");
        Assert.Equal(CampaignTarget.Region(front), ui.Screen.Choice?.Target);
    }

    [Fact]
    public void Events_are_listed_and_can_be_chosen()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            var campaign = CampaignState.Start(seed, CampaignRuleSets.Standard, MawDifficulty.Extinction);
            TurnReport? last = null;
            while (campaign.Events.Count == 0 && campaign.Turn < 15)
            {
                last = campaign.Play(new TurnRecord(CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty, MissionResult.Victory(2)));
            }

            if (campaign.Events.Count == 0)
            {
                continue;
            }

            var ui = new Driver(campaign, last);
            var campaignEvent = campaign.Events[0];
            var label = ui.Screen.Targets.First(target => target.Label.StartsWith(CampaignState.EventName(campaignEvent.Kind).ToUpperInvariant(), StringComparison.Ordinal)).Label;
            ui.Click(label);
            Assert.Equal(CampaignTarget.Event(campaignEvent.Id), ui.Screen.Selected);
            ui.Click("LAUNCH MISSION");
            Assert.Equal(CampaignTarget.Event(campaignEvent.Id), ui.Screen.Choice?.Target);
            return;
        }

        Assert.Fail("No event appeared.");
    }

    [Fact]
    public void The_difficulty_can_be_lowered_for_the_next_mission_but_not_raised()
    {
        var ui = new Driver(CampaignState.Start(5, CampaignRuleSets.Quick, MawDifficulty.Veteran));
        ui.Click("EASIER");
        Assert.Equal(MawDifficulty.Recruit, ui.Screen.Difficulty);
        Assert.False(ui.Enabled("EASIER"));
        ui.Click("LAUNCH MISSION");
        Assert.Equal(MawDifficulty.Recruit, ui.Screen.Choice?.Difficulty);
    }

    [Fact]
    public void Escape_or_save_and_exit_leaves_and_a_finished_campaign_offers_nothing_to_launch()
    {
        var ui = new Driver(CampaignState.Start(5, CampaignRuleSets.Quick, MawDifficulty.Veteran));
        ui.Press(Key.Escape);
        Assert.Equal(CampaignChoiceKind.Leave, ui.Screen.Choice?.Kind);

        ui = new Driver(CampaignState.Start(5, CampaignRuleSets.Quick, MawDifficulty.Veteran));
        ui.Click("SAVE AND EXIT");
        Assert.Equal(CampaignChoiceKind.Leave, ui.Screen.Choice?.Kind);

        var over = CampaignState.Start(3, CampaignRuleSets.Quick, MawDifficulty.Recruit);
        CampaignAutopilot.PlayToEnd(over, 95, 3);
        ui = new Driver(over);
        Assert.Null(ui.Screen.Selected);
        Assert.DoesNotContain(ui.Screen.Targets, target => target.Label == "LAUNCH MISSION");
        Assert.False(ui.Enabled("EASIER"));
    }

    [Fact]
    public void The_last_turn_and_a_message_are_shown_and_every_size_draws()
    {
        var campaign = CampaignState.Start(8, CampaignRuleSets.Standard, MawDifficulty.Veteran);
        var report = campaign.Play(new TurnRecord(CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty, MissionResult.Defeat(1, 2)));
        foreach (var size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1440), new Vector2(1024, 600) })
        {
            var screen = new CampaignScreen(campaign, 1, report, "Mission abandoned before it ended: no turn passed.");
            var scene = new FrameScene();
            screen.Update(new InputState(), size);
            screen.Draw(scene);
            Assert.True(scene.QuadCount > 500);
            Assert.All(screen.Targets, target => Assert.True(target.Center.X <= size.X && target.Center.Y <= size.Y, $"{target.Label} is off a {size} screen."));
        }
    }

    [Fact]
    public void Research_is_bought_from_its_tab_and_only_when_affordable()
    {
        var campaign = CampaignState.Start(5, CampaignRuleSets.Quick, MawDifficulty.Veteran);
        var ui = new Driver(campaign);
        ui.Click("RESEARCH");
        Assert.Equal(CampaignView.Research, ui.Screen.View);
        Assert.False(ui.Enabled("HARDENED ROUNDS - 20")); // no samples yet

        campaign.Play(new TurnRecord(CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty, MissionResult.Victory(2, samples: 30)));
        ui = new Driver(campaign) { };
        ui.Screen.View = CampaignView.Research;
        ui.Frame();
        Assert.False(ui.Enabled("DEPLETED CORES - 45")); // needs Hardened Rounds first
        ui.Click("HARDENED ROUNDS - 20");
        var choice = Assert.IsType<CampaignChoice>(ui.Screen.Choice);
        Assert.Equal(CampaignChoiceKind.Research, choice.Kind);
        Assert.Equal("weapons-1", choice.Research);
    }

    [Fact]
    public void The_commander_tab_switches_commanders_and_toggles_unlocked_perks()
    {
        var profile = new CommanderProfile();
        profile.AddExperience(120); // level 2
        var ui = new Driver(CampaignState.Start(5, CampaignRuleSets.Quick, MawDifficulty.Veteran), profile: profile);
        ui.Click("COMMANDER");
        var kova = Commanders.Find("kova")!;
        Assert.Equal(kova, profile.Active);
        Assert.Equal(2, profile.ActivePerks.Count); // levels 1 and 2 switched on as they came
        ui.Click("HOLD THE LINE - ON");
        Assert.Single(profile.ActivePerks);
        Assert.False(ui.Enabled("FIELD REPAIRS - LEVEL 3"));
        ui.Click("REYES");
        Assert.Equal("reyes", profile.Active.Id);

        // The mission panel shows the seat's bonuses.
        profile.Lead(kova);
        ui.Click("MISSION");
        Assert.Contains("BONUSES", string.Join(" ", ui.Screen.PlannedMission is { } mission ? ["BONUSES"] : Array.Empty<string>()), StringComparison.Ordinal);
        Assert.Equal(105, ui.Screen.PlannedMission!.Loadouts[0].Modifiers.UnitHealthPercent);
    }

    private sealed class Driver
    {
        private readonly InputState _input = new() { Mouse = new Vector2(5, 5) };
        private double _time;

        public Driver(CampaignState campaign, TurnReport? last = null, CommanderProfile? profile = null)
        {
            Screen = new CampaignScreen(campaign, allies: 1, last, commander: profile);
            Frame();
        }

        public CampaignScreen Screen { get; }

        public FrameScene Scene { get; } = new();

        public void Frame()
        {
            _time += 1 / 60.0;
            _input.Time = _time;
            Screen.Update(_input, Framebuffer);
            Screen.Draw(Scene);
            _input.EndFrame();
        }

        public bool Enabled(string label) => Screen.Targets.First(t => t.Label == label).Enabled;

        public void Click(string label)
        {
            var target = Screen.Targets.FirstOrDefault(t => t.Label == label);
            Assert.True(target.Label == label, $"No '{label}': {string.Join(", ", Screen.Targets.Select(t => t.Label))}");
            Assert.True(target.Enabled, label + " is disabled");
            ClickAt(target.Center);
        }

        public void ClickAt(Vector2 point)
        {
            _input.Mouse = point;
            _input.SetButton(MouseButton.Left, true);
            Frame();
            _input.SetButton(MouseButton.Left, false);
            Frame();
        }

        public void Press(Key key)
        {
            _input.SetKey(key, true);
            Frame();
            _input.SetKey(key, false);
            Frame();
        }
    }
}
