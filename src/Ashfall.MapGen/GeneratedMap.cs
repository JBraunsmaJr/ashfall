using Ashfall.Core;
using Ashfall.Simulation.Map;

namespace Ashfall.MapGen;

/// <summary>A finished map: the grid, where each player's Construction Yard goes, and how it was made.</summary>
public sealed class GeneratedMap
{
    public required GridMap Map { get; init; }

    /// <summary>Top-left cell of each player's 3×3 Construction Yard, in seat order.</summary>
    public required IReadOnlyList<Cell> StartOrigins { get; init; }

    /// <summary>The settings asked for.</summary>
    public required MapSettings Requested { get; init; }

    /// <summary>The settings used, after the archetype's limits (see <see cref="Clamps"/>).</summary>
    public required MapSettings Effective { get; init; }

    /// <summary>One line per slider the archetype had to limit, for the lobby to show.</summary>
    public required IReadOnlyList<string> Clamps { get; init; }

    public required MapReport Report { get; init; }

    /// <summary>Generation attempts used (1 when the first layout passed validation).</summary>
    public required int Attempts { get; init; }

    /// <summary>Why each rejected attempt failed validation (its first failure), in order.</summary>
    public IReadOnlyList<string> Rejected { get; init; } = [];

    /// <summary>Every attempt failed, so this is the plain fallback layout (no cliffs, water or clutter).</summary>
    public required bool FellBack { get; init; }

    /// <summary>Rotational symmetry order: 2 or 4.</summary>
    public required int SymmetryOrder { get; init; }

    public int Players => StartOrigins.Count;

    public int Size => Map.Width;

    /// <summary>Hash of the grid as it is now (compared by peers before a match starts).</summary>
    public ulong Hash
    {
        get
        {
            var hasher = new StateHasher();
            Map.HashInto(ref hasher);
            return hasher.Value;
        }
    }
}
