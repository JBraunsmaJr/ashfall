using Ashfall.Core;
using Ashfall.MapGen;
using Ashfall.Simulation.Map;

namespace Ashfall.Session.Maps;

/// <summary>
/// The map a skirmish is played on, hand-made or generated: the grid, each player's Construction Yard
/// origin, and (for generated maps) the generator's report. Every peer builds it locally from the
/// match settings and compares <see cref="Hash"/> before the match starts.
/// </summary>
public sealed class MatchMap
{
    private MatchMap(GridMap grid, IReadOnlyList<Cell> startOrigins, int symmetryOrder, GeneratedMap? generated)
    {
        Grid = grid;
        StartOrigins = startOrigins;
        SymmetryOrder = symmetryOrder;
        Generated = generated;
        var hasher = new StateHasher();
        grid.HashInto(ref hasher);
        Hash = hasher.Value;
    }

    /// <summary>The grid; once a match starts on it, it is the live simulation map.</summary>
    public GridMap Grid { get; }

    public IReadOnlyList<Cell> StartOrigins { get; }

    /// <summary>Rotational symmetry: 2 for duels, 4 for three or four players.</summary>
    public int SymmetryOrder { get; }

    /// <summary>The generator's output, or null for a hand-made map.</summary>
    public GeneratedMap? Generated { get; }

    /// <summary>"Duel", "Quad", or the archetype and map code of a generated map.</summary>
    public string Name { get; private init; } = string.Empty;

    /// <summary>Hash of the grid as generated (the match then changes the grid as it plays).</summary>
    public ulong Hash { get; }

    /// <summary>Builds the map for <paramref name="players"/> players: generated when <paramref name="settings"/> is given.</summary>
    public static MatchMap Create(MapSettings? settings, int players)
    {
        if (settings is null)
        {
            var starts = Enumerable.Range(0, players).Select(player => SkirmishMaps.StartOrigin(players, player)).ToArray();
            return new MatchMap(SkirmishMaps.Create(players), starts, MapSymmetry.OrderFor(players), null) { Name = SkirmishMaps.NameFor(players) };
        }

        var generated = MapGenerator.Generate(settings, players);
        return new MatchMap(generated.Map, generated.StartOrigins, generated.SymmetryOrder, generated)
        {
            Name = $"{settings.Archetype.Name} {settings.ToCode()}",
        };
    }

    /// <summary>The hash every peer must agree on before a match on these settings starts.</summary>
    public static ulong HashOf(MapSettings? settings, int players) => Create(settings, players).Hash;
}
