using System.Globalization;
using Ashfall.Session;

namespace Ashfall.UI.Menu;

/// <summary>
/// What the menus remember between launches: your name, the last skirmish and host setup, and the
/// hosts you joined recently. Stored as plain <c>key=value</c> lines next to the network identity.
/// </summary>
public sealed record MenuSettings
{
    public const int MaxRecentHosts = 5;

    public string Name { get; init; } = "PLAYER";

    public ulong Seed { get; init; } = ProvingGround.DefaultSeed;

    /// <summary>Skirmish opponent: the scripted AI, or an idle one to learn the controls against.</summary>
    public bool IdleOpponent { get; init; }

    public int Humans { get; init; } = 2;

    public int Ais { get; init; }

    public int Port { get; init; } = HostAddress.DefaultPort;

    /// <summary>Most recent first, as typed (host or host:port).</summary>
    public IReadOnlyList<string> RecentHosts { get; init; } = [];

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Ashfall", "menu.cfg");

    public MenuSettings WithRecentHost(string address)
    {
        var recent = new List<string> { address };
        recent.AddRange(RecentHosts.Where(h => !string.Equals(h, address, StringComparison.OrdinalIgnoreCase)));
        return this with { RecentHosts = recent.Take(MaxRecentHosts).ToList() };
    }

    public string Serialize()
    {
        var lines = new List<string>
        {
            "name=" + Name,
            "seed=" + Seed.ToString(CultureInfo.InvariantCulture),
            "opponent=" + (IdleOpponent ? "idle" : "scripted"),
            "humans=" + Humans.ToString(CultureInfo.InvariantCulture),
            "ais=" + Ais.ToString(CultureInfo.InvariantCulture),
            "port=" + Port.ToString(CultureInfo.InvariantCulture),
        };
        lines.AddRange(RecentHosts.Select(h => "recent=" + h));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Reads settings, ignoring unknown or invalid lines so an old or hand-edited file still loads.</summary>
    public static MenuSettings Parse(string text)
    {
        var settings = new MenuSettings();
        var recent = new List<string>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var value = line[(eq + 1)..].Trim();
            switch (line[..eq].Trim())
            {
                case "name" when value.Length > 0:
                    settings = settings with { Name = value.Length > 16 ? value[..16] : value };
                    break;
                case "seed" when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed):
                    settings = settings with { Seed = seed };
                    break;
                case "opponent":
                    settings = settings with { IdleOpponent = value == "idle" };
                    break;
                case "humans" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var humans) && humans is >= 1 and <= 4:
                    settings = settings with { Humans = humans };
                    break;
                case "ais" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ais) && ais is >= 0 and <= 3:
                    settings = settings with { Ais = ais };
                    break;
                case "port" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535:
                    settings = settings with { Port = port };
                    break;
                case "recent" when HostAddress.TryParse(value, out _, out _) && recent.Count < MaxRecentHosts:
                    recent.Add(value);
                    break;
            }
        }

        // Keep the slot count playable even if the file disagrees with itself.
        if (settings.Humans + settings.Ais is < 2 or > 4)
        {
            settings = settings with { Humans = 2, Ais = 0 };
        }

        return settings with { RecentHosts = recent };
    }

    public static MenuSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new MenuSettings();
        }
        catch (IOException)
        {
            return new MenuSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new MenuSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, Serialize());
        }
        catch (IOException)
        {
            // Remembering settings is a convenience; never fail a launch over it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
