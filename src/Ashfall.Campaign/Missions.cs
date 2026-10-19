using Ashfall.MapGen;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign;

/// <summary>What a campaign mission asks of the team (GDD section 6).</summary>
public enum MissionKind
{
    /// <summary>Destroy every nest.</summary>
    Purge,

    /// <summary>Survive the waves. Played as a Purge until Holdout mode exists.</summary>
    Holdout,

    /// <summary>Destroy a theatre's hive. Played as a Purge against the hive's nests until the Hive Mother exists (M9).</summary>
    HiveAssault,

    /// <summary>Escort survivors across the map. Played as a Purge until escort missions exist.</summary>
    Escort,
}

/// <summary>What the team chooses to fight for on a turn: a region on the front, or an active event.</summary>
public readonly record struct CampaignTarget(bool IsEvent, int Id)
{
    public static CampaignTarget Region(int region) => new(false, region);

    public static CampaignTarget Event(int id) => new(true, id);

    public override string ToString() => (IsEvent ? "event " : "region ") + Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>An event on the world map, waiting for an answer until the end of turn <see cref="ExpiresAfterTurn"/>.</summary>
public sealed record CampaignEvent(int Id, CampaignEventKind Kind, int Region, int ExpiresAfterTurn);

/// <summary>
/// How a mission ended, as every peer derives it from the mission's end state (GDD section 7, "Saves").
/// </summary>
/// <param name="Won">The team met the mission's goal.</param>
/// <param name="NestsDestroyed">Nests the team destroyed.</param>
/// <param name="NestsStanding">Nests still standing at the end; on a loss they push the Maw's spread.</param>
/// <param name="Ticks">How long the mission lasted.</param>
public readonly record struct MissionResult(bool Won, int NestsDestroyed, int NestsStanding, long Ticks)
{
    public static MissionResult Victory(int nests, long ticks = 0) => new(true, nests, 0, ticks);

    public static MissionResult Defeat(int nestsDestroyed, int nestsStanding, long ticks = 0) => new(false, nestsDestroyed, nestsStanding, ticks);
}

/// <summary>One played turn, as a save stores it: the target, the difficulty it was played on, and how it went.</summary>
public sealed record TurnRecord(CampaignTarget Target, MawDifficulty Difficulty, MissionResult Result);

/// <summary>
/// A mission generated from a region's data (GDD section 7, "From region to mission"): everything the session needs to
/// build a normal co-op match. Nothing here is chosen by hand; the same campaign state always plans the same mission.
/// </summary>
public sealed record CampaignMission
{
    public required CampaignTarget Target { get; init; }

    public required int Region { get; init; }

    /// <summary>The event being answered, or null for a mission on the front.</summary>
    public CampaignEventKind? Event { get; init; }

    public required MissionKind Kind { get; init; }

    /// <summary>The turn this mission is played on (1 for the first).</summary>
    public required int Turn { get; init; }

    /// <summary>Missions already played in this region.</summary>
    public required int Attempt { get; init; }

    /// <summary>The match seed: the campaign seed, region and attempt mixed together.</summary>
    public required ulong Seed { get; init; }

    /// <summary>The map: archetype from the terrain, sliders from the region (read-only in the lobby).</summary>
    public required MapSettings Map { get; init; }

    public required int Nests { get; init; }

    /// <summary>The tier the nests start at: 1 Spawning Pit, 2 Brood Nest, 3 Behemoth Den.</summary>
    public required int NestTier { get; init; }

    public required MawDifficulty Difficulty { get; init; }

    /// <summary>Extra starting credits per player, from liberated neighbours.</summary>
    public required int BonusCredits { get; init; }

    /// <summary>Extra starting harvesters per player, from liberated neighbours.</summary>
    public required int BonusHarvesters { get; init; }

    /// <summary>A one-paragraph briefing for the campaign screen and the lobby.</summary>
    public required string Briefing { get; init; }
}
