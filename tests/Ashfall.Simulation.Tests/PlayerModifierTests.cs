﻿using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using Ashfall.Simulation.Systems;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

/// <summary>Campaign bonuses in a match: damage, unit, harvester and structure health, repair costs; hashed only when set.</summary>
public class PlayerModifierTests
{
    private static GameContent C => TestMatch.Content;

    private static GameSimulation With(PlayerModifiers modifiers) =>
        new(7, new GridMap(32, 32), C,
            [new PlayerSetup(0, BulwarkContent.FactionId, Modifiers: modifiers), new PlayerSetup(1, BulwarkContent.FactionId)], victoryEnabled: false);

    [Fact]
    public void Bonuses_raise_health_when_made_and_damage_when_fired()
    {
        var sim = With(new PlayerModifiers(DamagePercent: 150, UnitHealthPercent: 120, StructureHealthPercent: 200, HarvesterHealthPercent: 150));
        var rifle = C.Unit(Unit(BulwarkContent.Rifleman));
        var ours = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(10, 10).Center);
        var harvester = sim.SpawnUnit(0, Unit(BulwarkContent.Harvester), new Cell(4, 4).Center);
        var theirs = sim.SpawnUnit(1, Unit(BulwarkContent.Rifleman), new Cell(13, 10).Center);
        var plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(20, 20));
        Assert.Equal(rifle.Health * 120 / 100, sim.World.Get<Health>(ours).Max);
        Assert.Equal(rifle.Health, sim.World.Get<Health>(theirs).Max);
        Assert.Equal(C.Unit(Unit(BulwarkContent.Harvester)).Health * 120 / 100 * 150 / 100, sim.World.Get<Health>(harvester).Max);
        Assert.Equal(C.Structure(Structure(BulwarkContent.PowerPlant)).Health * 2, sim.World.Get<Health>(plant).Max);

        // Both fire once at each other: ours hits half again as hard.
        RunUntil(sim, s => s.World.Get<Health>(theirs).Current < rifle.Health && s.World.Get<Health>(ours).Current < s.World.Get<Health>(ours).Max, 40);
        var dealt = rifle.Health - sim.World.Get<Health>(theirs).Current;
        var taken = sim.World.Get<Health>(ours).Max - sim.World.Get<Health>(ours).Current;
        Assert.Equal(PlayerModifiers.Apply(taken, 150), dealt);
    }

    [Fact]
    public void Repairs_cost_less_with_the_bonus()
    {
        var rules = C.Economy;
        var price = C.Structure(Structure(BulwarkContent.PowerPlant)).Cost;
        var normal = With(PlayerModifiers.None);
        var cheap = With(new PlayerModifiers(RepairCostPercent: 50));
        foreach (var sim in new[] { normal, cheap })
        {
            var plant = sim.PlaceStructure(0, Structure(BulwarkContent.PowerPlant), new Cell(10, 10));
            sim.World.Get<Health>(plant).Current /= 2;
            sim.World.Get<StructureInfo>(plant).Repairing = true;
        }

        var normalStart = normal.Players[0].Credits;
        var cheapStart = cheap.Players[0].Credits;
        Run(normal, rules.RepairIntervalTicks * 3);
        Run(cheap, rules.RepairIntervalTicks * 3);
        var normalSpent = normalStart - normal.Players[0].Credits;
        var cheapSpent = cheapStart - cheap.Players[0].Credits;
        Assert.True(normalSpent > 0);
        Assert.InRange(cheapSpent, (normalSpent / 2) - 2, (normalSpent / 2) + 2);
        _ = price;
    }

    [Fact]
    public void A_match_without_bonuses_hashes_as_before_and_bonuses_change_the_hash()
    {
        GameSimulation Make(PlayerModifiers? modifiers) =>
            new(7, new GridMap(16, 16), C, [new PlayerSetup(0, BulwarkContent.FactionId, Modifiers: modifiers), new PlayerSetup(1, BulwarkContent.FactionId)], false);

        Assert.Equal(Make(null).ComputeStateHash(), Make(PlayerModifiers.None).ComputeStateHash());
        Assert.NotEqual(Make(null).ComputeStateHash(), Make(new PlayerModifiers(DamagePercent: 110)).ComputeStateHash());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerModifiers(DamagePercent: 0).Validate());
    }
}
