using Ashfall.Content;
using Ashfall.Core;

namespace Ashfall.Simulation.Tests;

public class ContentTests
{
    [Theory]
    [InlineData(15, Warhead.SmallArms, ArmorType.None, 15)]
    [InlineData(15, Warhead.SmallArms, ArmorType.Heavy, 1)]
    [InlineData(45, Warhead.AntiArmour, ArmorType.Light, 45)]
    [InlineData(45, Warhead.AntiArmour, ArmorType.None, 11)]
    [InlineData(40, Warhead.AntiArmour, ArmorType.Structure, 24)]
    [InlineData(100, Warhead.Flak, ArmorType.Structure, 0)]
    public void Damage_is_scaled_by_warhead_against_armour_and_truncated(int baseDamage, Warhead warhead, ArmorType armor, int expected)
    {
        Assert.Equal(expected, DamageTable.Apply(baseDamage, warhead, armor));
    }

    [Fact]
    public void Every_warhead_and_armour_pair_has_a_percentage()
    {
        foreach (var warhead in Enum.GetValues<Warhead>())
        {
            foreach (var armor in Enum.GetValues<ArmorType>())
            {
                Assert.InRange(DamageTable.Percent(warhead, armor), 0, 200);
            }
        }
    }

    [Fact]
    public void The_bulwark_roster_is_complete_and_consistent()
    {
        var content = GameContent.Standard;
        var faction = content.Faction(content.FactionIndex(BulwarkContent.FactionId));

        Assert.Equal(BulwarkContent.ConstructionYard, faction.StartingStructureId);
        foreach (var role in Enum.GetValues<StructureRole>())
        {
            Assert.Contains(faction.StructureIds, id => content.Structure(content.StructureIndex(id)).Role == role);
        }

        foreach (var id in faction.UnitIds)
        {
            var unit = content.Unit(content.UnitIndex(id));
            Assert.Contains(faction.StructureIds, structure => content.Structure(content.StructureIndex(structure)).Role == unit.ProducedBy);
            Assert.True(unit.Weapon is not null || unit.HarvestCapacity > 0, $"{unit.Name} neither fights nor harvests.");
        }

        Assert.Equal(BulwarkContent.Harvester, content.Structure(content.StructureIndex(BulwarkContent.Refinery)).FreeUnitId);
    }

    [Fact]
    public void Content_with_a_duplicate_id_or_dangling_reference_is_rejected()
    {
        var standard = GameContent.Standard;
        var units = standard.Units.ToList();
        units.Add(units[0]);
        Assert.Throws<InvalidOperationException>(() => new GameContent(units, standard.Structures, standard.Factions, standard.Economy));

        var structures = standard.Structures.ToList();
        structures[0] = structures[0] with { FreeUnitId = "nobody.ghost" };
        Assert.Throws<KeyNotFoundException>(() => new GameContent(standard.Units, structures, standard.Factions, standard.Economy));

        var badUnits = standard.Units.ToList();
        badUnits[0] = badUnits[0] with { SpeedPerTick = Fix64.Zero };
        Assert.Throws<InvalidOperationException>(() => new GameContent(badUnits, standard.Structures, standard.Factions, standard.Economy));
    }

    [Fact]
    public void Unknown_ids_are_reported_by_name()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => GameContent.Standard.UnitIndex("bulwark.mammoth"));
        Assert.Contains("bulwark.mammoth", error.Message, StringComparison.Ordinal);
    }
}
