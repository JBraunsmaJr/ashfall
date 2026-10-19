﻿using System.Diagnostics;
using System.Numerics;
using Ashfall.Campaign;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Session;
using Ashfall.Session.Campaigns;
using Ashfall.Simulation.Horde;
using Ashfall.UI.Campaigns;
using Ashfall.UI.Menu;
using Brutal.GlfwApi;

namespace Ashfall.Client;

/// <summary>
/// The campaign loop (GDD section 7): the campaign screen until the player launches a mission, the mission as an
/// ordinary match with AI allies, then the result applied to the world and saved, and back to the map; until the
/// player leaves or the window closes. A mission left before it ends is abandoned: no turn passes.
/// </summary>
internal static class CampaignClient
{
    /// <summary>Opened from the command line: a new campaign from --campaign-code, else the saved one, else a new one.</summary>
    public static async Task<int> Start(GlfwWindow window, VulkanRenderer renderer, GlfwInput input, ClientOptions options)
    {
        var path = options.CampaignSave ?? CampaignFiles.DefaultPath;
        CampaignState campaign;
        string? message = null;
        if (options.CampaignCode is { } text && CampaignCode.TryParse(text, out var code))
        {
            campaign = CampaignState.Start(code);
        }
        else if (CampaignFiles.TryLoad(path, out var loaded, out var error))
        {
            campaign = loaded!.Campaign;
            message = loaded.Warnings.Count > 0 ? string.Join(" ", loaded.Warnings) : null;
        }
        else
        {
            campaign = CampaignState.Start((uint)(options.Seed & uint.MaxValue), CampaignRuleSets.Standard, options.Difficulty);
            message = File.Exists(path) ? error + " A new campaign was started; the old save is kept as " + Path.GetFileName(path) + ".bak." : null;
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
        }

        await Run(window, renderer, input, options, campaign, path, options.Allies, message).ConfigureAwait(false);
        return 0;
    }

    /// <summary>Opened from the main menu's campaign page. Returns a message for the menu, if any.</summary>
    public static async Task<string?> FromMenu(GlfwWindow window, VulkanRenderer renderer, GlfwInput input, ClientOptions options, MenuChoice choice)
    {
        var settings = choice.Settings;
        var path = CampaignFiles.DefaultPath;
        CampaignState campaign;
        string? message = null;
        if (choice.NewCampaign)
        {
            var rules = CampaignRuleSets.Find(settings.CampaignRules) ?? CampaignRuleSets.Standard;
            campaign = CampaignState.Start(settings.CampaignSeed, rules, settings.CampaignDifficulty);
        }
        else if (CampaignFiles.TryLoad(path, out var loaded, out var error))
        {
            campaign = loaded!.Campaign;
            message = loaded.Warnings.Count > 0 ? string.Join(" ", loaded.Warnings) : null;
        }
        else
        {
            return error;
        }

        await Run(window, renderer, input, options, campaign, path, settings.CampaignAllies, message).ConfigureAwait(false);
        return null;
    }

    public static async Task Run(
        GlfwWindow window, VulkanRenderer renderer, GlfwInput input, ClientOptions options, CampaignState campaign, string path, int allies, string? message)
    {
        CampaignFiles.Save(campaign, path);
        var profile = CommanderFiles.Load(CommanderFiles.DefaultPath);
        TurnReport? lastTurn = null;
        CampaignTarget? selected = null;
        var view = CampaignView.Mission;
        var played = 0;
        while (!window.ShouldClose)
        {
            // --autopilot with --campaign-missions plays that many missions unattended (smoke tests, demos).
            var autopilot = options.Autopilot && played < options.CampaignMissions && campaign.Outcome == CampaignOutcome.InProgress;
            CampaignChoice? choice;
            if (autopilot)
            {
                CampaignAutopilot.BuyResearch(campaign);
                choice = new CampaignChoice(CampaignChoiceKind.Launch, CampaignAutopilot.ChooseTarget(campaign), campaign.Difficulty);
            }
            else
            {
                var screen = new CampaignScreen(campaign, allies, lastTurn, message, profile) { Initial = selected, View = view };
                choice = Show(window, renderer, input, options, screen);
                view = screen.View;
                selected = screen.Selected;
                CommanderFiles.Save(profile, CommanderFiles.DefaultPath); // perks chosen on the screen
            }

            message = null;
            if (choice is null || choice.Kind == CampaignChoiceKind.Leave)
            {
                break;
            }

            if (choice.Kind == CampaignChoiceKind.Research)
            {
                campaign.Research(choice.Research!);
                CampaignFiles.Save(campaign, path);
                continue;
            }

            // Seat 0 is this player, with their commander; the AI allies fill the other seats.
            var mission = campaign.PlanMission(choice.Target, choice.Difficulty, 1 + allies, [profile.Bonus()]);
            var spec = CampaignMatches.ToMatchSpec(mission).WithController(0, PlayerController.Human);
            window.Title = "Ashfall - " + campaign.World.Regions[mission.Region].Name;
            var result = await SkirmishClient.Run(window, renderer, input, options with { Frames = 0, UntilTick = 0, Screenshot = null }, spec, mission, campaign.Rules)
                .ConfigureAwait(false);
            window.Title = "Ashfall";
            played++;
            if (result is not { } outcome)
            {
                message = "Mission abandoned before it ended: no turn passed.";
                continue;
            }

            lastTurn = campaign.Play(new TurnRecord(choice.Target, choice.Difficulty, outcome));
            CampaignFiles.Save(campaign, path);
            var experience = Commanders.ExperienceFor(outcome);
            if (profile.AddExperience(experience) is { } level)
            {
                message = $"{profile.Active.Name} reached level {level}: new perks unlocked.";
            }

            CommanderFiles.Save(profile, CommanderFiles.DefaultPath);
            Console.WriteLine($"Campaign turn {campaign.Turn}: {(outcome.Won ? "won" : "lost")} {campaign.World.Regions[mission.Region].Name}, {outcome.Samples} samples, +{experience} experience; {CampaignFiles.Summary(campaign)}");
        }

        CampaignFiles.Save(campaign, path);
    }

    private static CampaignChoice? Show(GlfwWindow window, VulkanRenderer renderer, GlfwInput input, ClientOptions options, CampaignScreen screen)
    {
        var scene = new FrameScene();
        var clock = Stopwatch.StartNew();
        input.State.EndFrame();
        while (!window.ShouldClose && screen.Choice is null)
        {
            if (options.Frames > 0 && renderer.FramesPresented >= options.Frames)
            {
                break;
            }

            Glfw.PollEvents();
            input.Poll();
            input.State.Time = clock.Elapsed.TotalSeconds;
            screen.Update(input.State, new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y));
            screen.Draw(scene);
            if (options.Screenshot is not null && options.Frames > 0 && renderer.FramesPresented == options.Frames - 1)
            {
                renderer.RequestCapture();
            }

            if (renderer.RenderFrame(scene) == FrameResult.Skipped)
            {
                Thread.Sleep(5);
            }

            input.State.EndFrame();
        }

        if (options.Screenshot is not null && screen.Choice is null && renderer.LastCapture is { } capture)
        {
            PngEncoder.WriteFile(options.Screenshot, capture);
            Console.WriteLine($"Saved {capture.Size} screenshot to {options.Screenshot}.");
        }

        return screen.Choice;
    }
}
