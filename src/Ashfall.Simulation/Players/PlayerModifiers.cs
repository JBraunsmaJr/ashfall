﻿using Ashfall.Core;

namespace Ashfall.Simulation.Players;

/// <summary>
/// Lasting bonuses a player brings into a match: campaign research and commander perks (GDD section 7). Percentages are
/// of the normal value (100 = unchanged). Applied when units and structures are made, when weapons fire and when
/// repairs are paid for.
/// </summary>
/// <param name="DamagePercent">Damage dealt by the player's units and defences.</param>
/// <param name="UnitHealthPercent">Hit points of the player's units.</param>
/// <param name="StructureHealthPercent">Hit points of the player's structures.</param>
/// <param name="HarvesterHealthPercent">Hit points of the player's harvesters (on top of <paramref name="UnitHealthPercent"/>).</param>
/// <param name="RepairCostPercent">What repairs cost.</param>
public readonly record struct PlayerModifiers(
    int DamagePercent = 100,
    int UnitHealthPercent = 100,
    int StructureHealthPercent = 100,
    int HarvesterHealthPercent = 100,
    int RepairCostPercent = 100)
{
    /// <summary>No bonuses at all.</summary>
    public static PlayerModifiers None => new(100, 100, 100, 100, 100);

    public bool IsNone => this == None;

    /// <summary>Both sets of bonuses together: each percentage's difference from 100 adds up.</summary>
    public PlayerModifiers Plus(PlayerModifiers other) => new(
        DamagePercent + other.DamagePercent - 100,
        UnitHealthPercent + other.UnitHealthPercent - 100,
        StructureHealthPercent + other.StructureHealthPercent - 100,
        HarvesterHealthPercent + other.HarvesterHealthPercent - 100,
        RepairCostPercent + other.RepairCostPercent - 100);

    /// <summary>Throws unless every percentage is within 10–500.</summary>
    public void Validate()
    {
        foreach (var percent in new[] { DamagePercent, UnitHealthPercent, StructureHealthPercent, HarvesterHealthPercent, RepairCostPercent })
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(percent, 10, nameof(PlayerModifiers));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 500, nameof(PlayerModifiers));
        }
    }

    /// <summary><paramref name="value"/> scaled by <paramref name="percent"/>, at least 1.</summary>
    public static int Apply(int value, int percent)
    {
        var scaled = (int)((long)value * percent / 100);
        return scaled > 1 ? scaled : 1;
    }

    public void HashInto(ref StateHasher hasher)
    {
        hasher.Add(DamagePercent);
        hasher.Add(UnitHealthPercent);
        hasher.Add(StructureHealthPercent);
        hasher.Add(HarvesterHealthPercent);
        hasher.Add(RepairCostPercent);
    }
}
