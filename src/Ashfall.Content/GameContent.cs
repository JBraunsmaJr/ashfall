using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>
/// Every definition a match can use, addressed by stable integer index. The simulation stores these
/// indices in components, so the order of the lists is part of the replay format: append only.
/// </summary>
public sealed class GameContent
{
    private readonly Dictionary<string, int> _unitIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _structureIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _factionIndices = new(StringComparer.Ordinal);

    public GameContent(
        IReadOnlyList<UnitDefinition> units,
        IReadOnlyList<StructureDefinition> structures,
        IReadOnlyList<FactionDefinition> factions,
        EconomyRules economy)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(factions);
        ArgumentNullException.ThrowIfNull(economy);

        Units = units;
        Structures = structures;
        Factions = factions;
        Economy = economy;

        for (var i = 0; i < units.Count; i++)
        {
            Register(_unitIndices, units[i].Id, i, "unit");
        }

        for (var i = 0; i < structures.Count; i++)
        {
            Register(_structureIndices, structures[i].Id, i, "structure");
        }

        for (var i = 0; i < factions.Count; i++)
        {
            Register(_factionIndices, factions[i].Id, i, "faction");
        }

        Validate();
    }

    public IReadOnlyList<UnitDefinition> Units { get; }

    public IReadOnlyList<StructureDefinition> Structures { get; }

    public IReadOnlyList<FactionDefinition> Factions { get; }

    public EconomyRules Economy { get; }

    /// <summary>The content shipped with the game. M1: the Bulwark only.</summary>
    public static GameContent Standard { get; } = BulwarkContent.Create();

    public int UnitIndex(string id) =>
        _unitIndices.TryGetValue(id, out var index) ? index : throw new KeyNotFoundException($"Unknown unit '{id}'.");

    public int StructureIndex(string id) =>
        _structureIndices.TryGetValue(id, out var index) ? index : throw new KeyNotFoundException($"Unknown structure '{id}'.");

    public int FactionIndex(string id) =>
        _factionIndices.TryGetValue(id, out var index) ? index : throw new KeyNotFoundException($"Unknown faction '{id}'.");

    public UnitDefinition Unit(int index) => Units[index];

    public StructureDefinition Structure(int index) => Structures[index];

    public FactionDefinition Faction(int index) => Factions[index];

    private static void Register(Dictionary<string, int> map, string id, int index, string kind)
    {
        if (!map.TryAdd(id, index))
        {
            throw new InvalidOperationException($"Duplicate {kind} id '{id}'.");
        }
    }

    private void Validate()
    {
        foreach (var unit in Units)
        {
            if (unit.Cost < 0 || unit.BuildTicks <= 0 || unit.Health <= 0 || unit.SpeedPerTick <= Fix64.Zero)
            {
                throw new InvalidOperationException($"Unit '{unit.Id}' has a non-positive cost, build time, health or speed.");
            }
        }

        foreach (var structure in Structures)
        {
            if (structure.Width <= 0 || structure.Height <= 0 || structure.Health <= 0)
            {
                throw new InvalidOperationException($"Structure '{structure.Id}' has an empty footprint or no health.");
            }

            if (structure.FreeUnitId is not null)
            {
                UnitIndex(structure.FreeUnitId);
            }
        }

        foreach (var faction in Factions)
        {
            StructureIndex(faction.StartingStructureId);
            foreach (var id in faction.StructureIds)
            {
                StructureIndex(id);
            }

            foreach (var id in faction.UnitIds)
            {
                UnitIndex(id);
            }
        }
    }
}
