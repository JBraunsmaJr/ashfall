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
        foreach (var role in Enum.GetValues<StructureRole>().Where(role => role != StructureRole.Nest))
        {
            Assert.Contains(faction.StructureIds, id => content.Structure(content.StructureIndex(id)).Role == role);
        }

        foreach (var id in faction.UnitIds)
        {
            var unit = content.Unit(content.UnitIndex(id));
            Assert.Contains(faction.StructureIds, structure => content.Structure(content.StructureIndex(structure)).Role == unit.ProducedBy);
            Assert.True(unit.Weapon is not null || unit.HarvestCapacity > 0 || unit.IsMedic, $"{unit.Name} neither fights, harvests nor heals.");
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

    [Theory]
    [InlineData(20, Warhead.Flame, ArmorType.Biomass, 35)]
    [InlineData(20, Warhead.Flame, ArmorType.Carapace, 5)]
    [InlineData(40, Warhead.AntiArmour, ArmorType.Biomass, 8)]
    [InlineData(40, Warhead.AntiArmour, ArmorType.Carapace, 40)]
    [InlineData(8, Warhead.Rending, ArmorType.Biomass, 0)]
    [InlineData(10, Warhead.Acid, ArmorType.Structure, 15)]
    public void No_single_warhead_answers_the_maw(int baseDamage, Warhead warhead, ArmorType armor, int expected)
    {
        Assert.Equal(expected, DamageTable.Apply(baseDamage, warhead, armor));
    }

    [Fact]
    public void The_standard_content_carries_the_maw_roster()
    {
        var content = GameContent.Standard;
        var skitter = content.Creature(content.CreatureIndex(MawContent.Skitter));

        Assert.Equal(ArmorType.Biomass, skitter.Armor);
        Assert.Equal(Warhead.Rending, skitter.Weapon.Warhead);
        Assert.True(skitter.SpeedPerTick > content.Unit(content.UnitIndex(BulwarkContent.Rifleman)).SpeedPerTick, "Chaff must outrun infantry.");
    }

    [Fact]
    public void Creatures_change_the_content_hash_and_invalid_creatures_are_rejected()
    {
        var standard = GameContent.Standard;
        var withoutMaw = new GameContent(standard.Units, standard.Structures, standard.Factions, standard.Economy);
        Assert.Empty(withoutMaw.Creatures);
        Assert.NotEqual(standard.ComputeHash(), withoutMaw.ComputeHash());

        var lame = standard.Creatures.Select(creature => creature with { SpeedPerTick = Fix64.Zero }).ToList();
        Assert.Throws<InvalidOperationException>(() => withoutMaw.WithCreatures(lame));

        var twice = standard.Creatures.Concat(standard.Creatures).ToList();
        Assert.Throws<InvalidOperationException>(() => withoutMaw.WithCreatures(twice));
    }

    [Fact]
    public void The_spawning_pit_is_a_carapace_nest_that_breeds_skitters()
    {
        var content = GameContent.Standard;
        var pitType = content.StructureIndex(MawContent.SpawningPit);
        var pit = content.Structure(pitType);
        var nest = content.NestFor(pitType)!;

        Assert.Equal(StructureRole.Nest, pit.Role);
        Assert.Equal(ArmorType.Carapace, pit.Armor);
        Assert.Equal([new SpawnWeight(MawContent.Skitter, 1)], nest.Spawns);
        Assert.Equal(MawContent.BroodNest, nest.EvolvesTo);
        Assert.Null(content.NestFor(content.StructureIndex(BulwarkContent.Barracks)));
        Assert.Equal(ArmorType.Structure, content.Structure(content.StructureIndex(BulwarkContent.Barracks)).Armor);
        Assert.DoesNotContain(content.Faction(0).StructureIds, id => id == MawContent.SpawningPit);
    }

    [Fact]
    public void Nests_must_name_a_nest_structure_and_a_known_creature()
    {
        var standard = GameContent.Standard;
        var nest = standard.Nests[0];
        GameContent With(NestDefinition changed) =>
            new(standard.Units, standard.Structures, standard.Factions, standard.Economy, standard.Creatures, [changed, .. standard.Nests.Skip(1)]);

        Assert.Throws<InvalidOperationException>(() => With(nest with { StructureId = BulwarkContent.Barracks }));
        Assert.Throws<KeyNotFoundException>(() => With(nest with { Spawns = [new SpawnWeight("maw.ghost", 1)] }));
        Assert.Throws<InvalidOperationException>(() => With(nest with { Spawns = [] }));
        Assert.Throws<InvalidOperationException>(() => With(nest with { Spawns = [new SpawnWeight(MawContent.Skitter, 0)] }));
        Assert.Throws<InvalidOperationException>(() => With(nest with { StartingBiomass = nest.MaxBiomass + 1 }));
        Assert.NotEqual(standard.ComputeHash(), With(nest with { FeedAmount = nest.FeedAmount + 1 }).ComputeHash());
    }

    [Fact]
    public void Nests_evolve_pit_to_brood_nest_to_behemoth_den_in_place()
    {
        var content = GameContent.Standard;
        string[] chain = [MawContent.SpawningPit, MawContent.BroodNest, MawContent.BehemothDen];
        for (var tier = 0; tier < chain.Length; tier++)
        {
            var type = content.StructureIndex(chain[tier]);
            var nest = content.NestFor(type)!;
            Assert.Equal(tier + 1, nest.Spawns.Count);
            Assert.Equal(tier < chain.Length - 1 ? chain[tier + 1] : null, nest.EvolvesTo);
            if (tier > 0)
            {
                Assert.True(content.Structure(type).Health > content.Structure(content.StructureIndex(chain[tier - 1])).Health);
            }
        }

        var siegebeast = content.Creature(content.CreatureIndex(MawContent.Siegebeast));
        Assert.Equal((ArmorType.Carapace, 8), (siegebeast.Armor, siegebeast.CellWeight));
        var spitter = content.Creature(content.CreatureIndex(MawContent.Spitter));
        Assert.True(spitter.Weapon.Range > content.Unit(content.UnitIndex(BulwarkContent.Rifleman)).Weapon!.Range);
    }

    [Fact]
    public void Unknown_ids_are_reported_by_name()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => GameContent.Standard.UnitIndex("bulwark.mammoth"));
        Assert.Contains("bulwark.mammoth", error.Message, StringComparison.Ordinal);
    }
}
