﻿using Ashfall.Campaign;

namespace Ashfall.Session.Campaigns;

/// <summary>
/// The player's commander profile on disk (GDD section 7): it belongs to the player, not a campaign, so it lives next to
/// their menu settings and carries into every campaign they play or join.
/// </summary>
public static class CommanderFiles
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Ashfall", "commander.cfg");

    /// <summary>The profile at <paramref name="path"/>, or a fresh one if there is none or it cannot be read.</summary>
    public static CommanderProfile Load(string path)
    {
        try
        {
            return File.Exists(path) ? CommanderProfile.Parse(File.ReadAllText(path)) : new CommanderProfile();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CommanderProfile();
        }
    }

    public static void Save(CommanderProfile profile, string path)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, profile.Serialize());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing a little commander progress must never stop a campaign.
        }
    }
}
