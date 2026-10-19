﻿using System.Globalization;
using System.Text;
using Ashfall.Simulation.Players;

namespace Ashfall.Campaign;

/// <summary>A commander perk: unlocked at a commander level, active when chosen (at most five at once).</summary>
/// <param name="Level">The commander level that unlocks it.</param>
/// <param name="Modifiers">Bonuses it gives every mission.</param>
/// <param name="Credits">Extra starting credits.</param>
/// <param name="Harvesters">Extra starting harvesters.</param>
/// <param name="DetachmentSlots">Extra units the player may keep between missions.</param>
public sealed record PerkDefinition(
    string Id, string Name, string Description, int Level, PlayerModifiers Modifiers, int Credits = 0, int Harvesters = 0, int DetachmentSlots = 0);

/// <summary>A commander a player can lead with (GDD section 7, "Commanders"), tied to a faction.</summary>
public sealed record CommanderDefinition(string Id, string Name, string Title, string FactionId, IReadOnlyList<PerkDefinition> Perks);

/// <summary>What a commander's active perks add up to for one mission.</summary>
public readonly record struct CommanderBonus(PlayerModifiers Modifiers, int Credits, int Harvesters, int DetachmentSlots)
{
    public static CommanderBonus None => new(PlayerModifiers.None, 0, 0, 0);
}

/// <summary>
/// The commanders this build knows, and how they grow: experience from every mission, a level every so often, a perk
/// per level. Bulwark only until the other factions arrive (M9).
/// </summary>
public static class Commanders
{
    /// <summary>Most perks active at once.</summary>
    public const int MaxActivePerks = 5;

    /// <summary>Experience needed for each level (level 1 at 0).</summary>
    public static IReadOnlyList<int> LevelThresholds { get; } = [0, 100, 250, 450, 700, 1_000, 1_400];

    public static IReadOnlyList<CommanderDefinition> All { get; } =
    [
        new("kova", "Marshal Kova", "The Anchor: walls that hold and armour that lasts", "bulwark",
        [
            new("kova-hold", "Hold the Line", "+15% hit points for structures.", 1, new PlayerModifiers(StructureHealthPercent: 115)),
            new("kova-ranks", "Iron Ranks", "+5% hit points for units.", 2, new PlayerModifiers(UnitHealthPercent: 105)),
            new("kova-repair", "Field Repairs", "Repairs cost 20% less.", 3, new PlayerModifiers(RepairCostPercent: 80)),
            new("kova-cadre", "Veteran Cadre", "Keep one more unit between missions.", 4, PlayerModifiers.None, DetachmentSlots: 1),
            new("kova-bastion", "Bastion", "+15% more hit points for structures.", 5, new PlayerModifiers(StructureHealthPercent: 115)),
            new("kova-stand", "Stand Fast", "+10% hit points for units.", 6, new PlayerModifiers(UnitHealthPercent: 110)),
            new("kova-reserve", "Reserve Column", "Keep two more units between missions.", 7, PlayerModifiers.None, DetachmentSlots: 2),
        ]),
        new("reyes", "Colonel Reyes", "The Hunter: firepower first, questions later", "bulwark",
        [
            new("reyes-gunnery", "Gunnery Drill", "+5% damage.", 1, new PlayerModifiers(DamagePercent: 105)),
            new("reyes-escort", "Armed Escorts", "+20% hit points for harvesters.", 2, new PlayerModifiers(HarvesterHealthPercent: 120)),
            new("reyes-fire", "Fire Superiority", "+5% more damage.", 3, new PlayerModifiers(DamagePercent: 105)),
            new("reyes-cadre", "Strike Cadre", "Keep one more unit between missions.", 4, PlayerModifiers.None, DetachmentSlots: 1),
            new("reyes-heavy", "Heavy Ordnance", "+10% damage.", 5, new PlayerModifiers(DamagePercent: 110)),
            new("reyes-plate", "Assault Plating", "+5% hit points for units.", 6, new PlayerModifiers(UnitHealthPercent: 105)),
            new("reyes-final", "No Retreat", "+5% damage and +5% hit points for units.", 7, new PlayerModifiers(DamagePercent: 105, UnitHealthPercent: 105)),
        ]),
        new("osei", "Major Osei", "The Quartermaster: more of everything, sooner", "bulwark",
        [
            new("osei-funds", "Discretionary Funds", "+500 starting credits.", 1, PlayerModifiers.None, Credits: 500),
            new("osei-haulers", "Spare Haulers", "An extra harvester at the start.", 2, PlayerModifiers.None, Harvesters: 1),
            new("osei-escort", "Convoy Plating", "+20% hit points for harvesters.", 3, new PlayerModifiers(HarvesterHealthPercent: 120)),
            new("osei-salvage", "Salvage Crews", "Repairs cost 25% less.", 4, new PlayerModifiers(RepairCostPercent: 75)),
            new("osei-reserves", "Strategic Reserves", "+750 more starting credits.", 5, PlayerModifiers.None, Credits: 750),
            new("osei-cadre", "Quartermaster's Picks", "Keep one more unit between missions.", 6, PlayerModifiers.None, DetachmentSlots: 1),
            new("osei-fleet", "Hauler Fleet", "Another extra harvester at the start.", 7, PlayerModifiers.None, Harvesters: 1),
        ]),
    ];

    public static CommanderDefinition? Find(string? id)
    {
        foreach (var commander in All)
        {
            if (commander.Id == id)
            {
                return commander;
            }
        }

        return null;
    }

    /// <summary>The level <paramref name="experience"/> reaches.</summary>
    public static int LevelOf(int experience)
    {
        var level = 0;
        foreach (var threshold in LevelThresholds)
        {
            if (experience >= threshold)
            {
                level++;
            }
        }

        return level;
    }

    /// <summary>Experience for the level after <paramref name="level"/>, or null at the top.</summary>
    public static int? NextThreshold(int level) => level < LevelThresholds.Count ? LevelThresholds[level] : null;

    /// <summary>Experience a mission gives: more for a win, and for every nest destroyed. Even a loss teaches something.</summary>
    public static int ExperienceFor(MissionResult result) => (result.Won ? 100 : 40) + (10 * result.NestsDestroyed);
}

/// <summary>
/// One player's commanders (GDD section 7): which one leads, and each one's experience and chosen perks. It belongs to
/// the player, not to a campaign, so it carries into every campaign they join; it is stored on their machine as plain
/// <c>key=value</c> lines. In a match it only shows up as the bonuses of its active perks.
/// </summary>
public sealed class CommanderProfile
{
    private readonly SortedDictionary<string, int> _experience = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, List<string>> _perks = new(StringComparer.Ordinal);

    /// <summary>The commander leading this player's forces.</summary>
    public CommanderDefinition Active { get; private set; } = Commanders.All[0];

    public int ExperienceOf(CommanderDefinition commander) => _experience.TryGetValue(commander?.Id ?? string.Empty, out var xp) ? xp : 0;

    public int LevelOf(CommanderDefinition commander) => Commanders.LevelOf(ExperienceOf(commander));

    /// <summary>
    /// The active commander's chosen perks, unlocked ones only, in the order they were chosen. Until the player chooses,
    /// every unlocked perk is on (up to five).
    /// </summary>
    public IReadOnlyList<PerkDefinition> ActivePerks
    {
        get
        {
            var level = LevelOf(Active);
            var chosen = _perks.TryGetValue(Active.Id, out var ids) ? ids : Active.Perks.Select(perk => perk.Id).ToList();
            return Active.Perks.Where(perk => perk.Level <= level && chosen.Contains(perk.Id))
                .OrderBy(perk => chosen.IndexOf(perk.Id))
                .Take(Commanders.MaxActivePerks)
                .ToList();
        }
    }

    public bool IsActive(PerkDefinition perk) => ActivePerks.Contains(perk);

    public void Lead(CommanderDefinition commander)
    {
        ArgumentNullException.ThrowIfNull(commander);
        Active = commander;
    }

    /// <summary>Adds experience to the active commander; returns the new level if it went up, else null. New perks are taken while there is room.</summary>
    public int? AddExperience(int experience)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(experience);
        var before = LevelOf(Active);
        _experience[Active.Id] = ExperienceOf(Active) + experience;
        var after = LevelOf(Active);
        if (after <= before)
        {
            return null;
        }

        if (_perks.ContainsKey(Active.Id))
        {
            foreach (var perk in Active.Perks.Where(perk => perk.Level > before && perk.Level <= after))
            {
                if (ActivePerks.Count < Commanders.MaxActivePerks && !IsActive(perk))
                {
                    Toggle(perk);
                }
            }
        }

        return after;
    }

    /// <summary>Turns an unlocked perk of the active commander on or off; false if it cannot be (locked, or five already on).</summary>
    public bool Toggle(PerkDefinition perk)
    {
        ArgumentNullException.ThrowIfNull(perk);
        if (!Active.Perks.Contains(perk) || perk.Level > LevelOf(Active))
        {
            return false;
        }

        if (!_perks.TryGetValue(Active.Id, out var chosen))
        {
            _perks[Active.Id] = chosen = ActivePerks.Select(active => active.Id).ToList(); // the defaults become a choice
        }

        if (chosen.Remove(perk.Id))
        {
            return true;
        }

        if (ActivePerks.Count >= Commanders.MaxActivePerks)
        {
            return false;
        }

        chosen.Add(perk.Id);
        return true;
    }

    /// <summary>What the active perks add up to.</summary>
    public CommanderBonus Bonus()
    {
        var bonus = CommanderBonus.None;
        foreach (var perk in ActivePerks)
        {
            bonus = new CommanderBonus(bonus.Modifiers.Plus(perk.Modifiers), bonus.Credits + perk.Credits, bonus.Harvesters + perk.Harvesters, bonus.DetachmentSlots + perk.DetachmentSlots);
        }

        return bonus;
    }

    public string Serialize()
    {
        var text = new StringBuilder();
        text.Append("commander=").Append(Active.Id).Append('\n');
        foreach (var (id, xp) in _experience)
        {
            text.Append(CultureInfo.InvariantCulture, $"xp.{id}={xp}\n");
        }

        foreach (var (id, perks) in _perks)
        {
            text.Append(CultureInfo.InvariantCulture, $"perks.{id}={string.Join(',', perks)}\n");
        }

        return text.ToString();
    }

    /// <summary>Reads a profile, ignoring anything unknown or invalid so an old or hand-edited file still loads.</summary>
    public static CommanderProfile Parse(string? text)
    {
        var profile = new CommanderProfile();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq];
            var value = line[(eq + 1)..];
            if (key == "commander" && Commanders.Find(value) is { } commander)
            {
                profile.Active = commander;
            }
            else if (key.StartsWith("xp.", StringComparison.Ordinal) && Commanders.Find(key[3..]) is { } experienced
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var xp))
            {
                profile._experience[experienced.Id] = xp;
            }
            else if (key.StartsWith("perks.", StringComparison.Ordinal) && Commanders.Find(key[6..]) is { } chooser)
            {
                profile._perks[chooser.Id] = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Where(id => chooser.Perks.Any(perk => perk.Id == id)).Distinct().ToList();
            }
        }

        return profile;
    }
}
