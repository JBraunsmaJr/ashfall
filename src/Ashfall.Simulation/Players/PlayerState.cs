using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Players;

/// <summary>Per-player economy and status. Simulation state: included in the state hash.</summary>
public sealed class PlayerState
{
    public PlayerState(int id, int team, int faction, int credits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfNegative(credits);
        Id = id;
        Team = team;
        Faction = faction;
        Credits = credits;
    }

    public int Id { get; }

    public int Team { get; }

    /// <summary>Index into <see cref="Content.GameContent.Factions"/>.</summary>
    public int Faction { get; }

    public int Credits { get; private set; }

    /// <summary>Power produced by completed structures, recomputed every tick.</summary>
    public int PowerSupply { get; internal set; }

    /// <summary>Power consumed by completed structures, recomputed every tick.</summary>
    public int PowerDemand { get; internal set; }

    public bool HasLowPower => PowerDemand > PowerSupply;

    /// <summary>The structure this player is currently constructing; one at a time, as in the classic sidebar.</summary>
    public Entity ConstructionSite { get; internal set; } = Entity.None;

    public bool Defeated { get; internal set; }

    public bool CanAfford(int cost) => cost <= Credits;

    internal void Spend(int cost)
    {
        if (cost < 0 || cost > Credits)
        {
            throw new InvalidOperationException($"Player {Id} cannot spend {cost} with {Credits} credits.");
        }

        Credits -= cost;
    }

    internal void Earn(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Credits += amount;
    }

    public void HashInto(ref StateHasher hasher)
    {
        hasher.Add(Id);
        hasher.Add(Team);
        hasher.Add(Faction);
        hasher.Add(Credits);
        hasher.Add(PowerSupply);
        hasher.Add(PowerDemand);
        hasher.Add(ConstructionSite.Index);
        hasher.Add(ConstructionSite.Generation);
        hasher.Add(Defeated);
    }
}
