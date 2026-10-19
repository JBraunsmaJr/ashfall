﻿using System.Globalization;
using Ashfall.Campaign;

namespace Ashfall.Session.Campaigns;

/// <summary>
/// Campaign saves on disk (GDD section 7): one plain-text file per campaign, written after every turn so a crash or a
/// closed window never loses more than the mission in progress. Every player keeps a copy; any of them can host.
/// </summary>
public static class CampaignFiles
{
    /// <summary>Where the client keeps the current campaign: next to the menu settings.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Ashfall", "campaign.save");

    /// <summary>Writes the save beside its final name first, then moves it into place, so a save is never half-written.</summary>
    public static void Save(CampaignState campaign, string path)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + ".tmp";
        File.WriteAllText(temporary, CampaignSave.Write(campaign));
        File.Move(temporary, full, overwrite: true);
    }

    /// <summary>Reads the save at <paramref name="path"/>; false (with why) if there is none or it cannot be read.</summary>
    public static bool TryLoad(string path, out LoadedCampaign? loaded, out string error)
    {
        loaded = null;
        error = string.Empty;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            error = "No saved campaign.";
            return false;
        }

        try
        {
            loaded = CampaignSave.Read(File.ReadAllText(path));
            return true;
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            error = "The saved campaign could not be read: " + e.Message;
            return false;
        }
    }

    /// <summary>One line for menus: "ASH-0G000-0041E, TURN 12, VETERAN, METER 43%, 2 HIVES LEFT".</summary>
    public static string Summary(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var state = campaign.Outcome switch
        {
            CampaignOutcome.Won => "WON",
            CampaignOutcome.Lost => "LOST",
            _ => string.Create(CultureInfo.InvariantCulture, $"{campaign.HivesLeft} HIVE{(campaign.HivesLeft == 1 ? string.Empty : "S")} LEFT"),
        };
        return string.Create(CultureInfo.InvariantCulture,
            $"{campaign.Code}, {campaign.Rules.Name.ToUpperInvariant()}, TURN {campaign.Turn}, {campaign.Difficulty.ToString().ToUpperInvariant()}, METER {campaign.Meter}%, {state}");
    }
}
