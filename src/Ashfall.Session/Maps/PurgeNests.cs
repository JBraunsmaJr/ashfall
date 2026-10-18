using Ashfall.Content;
using Ashfall.Simulation;
using Ashfall.Simulation.Map;

namespace Ashfall.Session.Maps;

/// <summary>
/// Seeds a Purge map with the Maw (GDD section 2): a Spawning Pit beside the fissure of each chosen Cindrite field,
/// so every nest feeds on a field that keeps regrowing until the players take it. Fields are chosen far from the
/// players' bases and spread out from each other; the fields at the players' own starts are never infested.
/// </summary>
public static class PurgeNests
{
    /// <summary>No nest's fissure is closer than this to a player's Construction Yard, in cells.</summary>
    public const int MinimumDistance = 20;

    // Where the nest's 3x3 footprint centre may sit relative to its fissure: off a corner first (so the vent stays
    // open beside the nest, as in the swarm fortress), then anywhere close.
    private static readonly (int Dx, int Dy)[] Offsets = BuildOffsets();

    /// <summary>
    /// Places up to <paramref name="count"/> Spawning Pits and returns how many were placed (fewer when the map has
    /// fewer suitable fields). The choice depends only on the map and the starts, so every peer agrees.
    /// </summary>
    public static int Place(GameSimulation simulation, IReadOnlyList<Cell> playerStarts, int count)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(playerStarts);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var map = simulation.Map;
        var pit = simulation.Content.StructureIndex(MawContent.SpawningPit);
        var footprint = simulation.Content.Structure(pit);
        var candidates = new List<Cell>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = new Cell(x, y);
                if (map.IsFissure(cell) && Nearest(cell, playerStarts) >= MinimumDistance * MinimumDistance)
                {
                    candidates.Add(cell);
                }
            }
        }

        // Farthest-point order: each nest goes on the field farthest from the bases and from the nests before it
        // (ties: the lowest cell index), so a handful of nests covers the map instead of clumping.
        var taken = new List<Cell>(playerStarts);
        var placed = 0;
        while (placed < count && candidates.Count > 0)
        {
            var best = 0;
            var bestDistance = -1;
            for (var i = 0; i < candidates.Count; i++)
            {
                var distance = Nearest(candidates[i], taken);
                if (distance > bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            var fissure = candidates[best];
            candidates.RemoveAt(best);
            if (TryFindOrigin(map, fissure, footprint.Width, footprint.Height, out var origin))
            {
                for (var y = 0; y < footprint.Height; y++)
                {
                    for (var x = 0; x < footprint.Width; x++)
                    {
                        map.SetCindrite(new Cell(origin.X + x, origin.Y + y), 0); // nothing grows under the nest
                    }
                }

                simulation.PlaceNest(pit, origin);
                taken.Add(fissure);
                placed++;
            }
        }

        return placed;
    }

    private static bool TryFindOrigin(GridMap map, Cell fissure, int width, int height, out Cell origin)
    {
        foreach (var (dx, dy) in Offsets)
        {
            var candidate = new Cell(fissure.X + dx - (width / 2), fissure.Y + dy - (height / 2));
            if (Fits(map, candidate, width, height))
            {
                origin = candidate;
                return true;
            }
        }

        origin = default;
        return false;
    }

    // Open, level ground with no structure and no fissure under the whole footprint (Cindrite is allowed: it is
    // cleared). A clear lane all round so creatures can leave and units can reach it.
    private static bool Fits(GridMap map, Cell origin, int width, int height)
    {
        var level = -1;
        for (var y = -1; y <= height; y++)
        {
            for (var x = -1; x <= width; x++)
            {
                var cell = new Cell(origin.X + x, origin.Y + y);
                if (!map.InBounds(cell))
                {
                    return false;
                }

                var inside = x >= 0 && y >= 0 && x < width && y < height;
                if (!inside)
                {
                    if (map.StructureAt(cell) >= 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (map.TerrainAt(cell) != Terrain.Clear || map.StructureAt(cell) >= 0 || map.IsFissure(cell))
                {
                    return false;
                }

                var ground = map.HeightAt(cell);
                if (level >= 0 && ground != level)
                {
                    return false;
                }

                level = ground;
            }
        }

        return true;
    }

    // Squared distance to the nearest of the given cells (int.MaxValue for none).
    private static int Nearest(Cell cell, IReadOnlyList<Cell> others)
    {
        var best = int.MaxValue;
        foreach (var other in others)
        {
            var dx = cell.X - other.X;
            var dy = cell.Y - other.Y;
            best = Math.Min(best, (dx * dx) + (dy * dy));
        }

        return best;
    }

    private static (int Dx, int Dy)[] BuildOffsets()
    {
        var offsets = new List<(int Dx, int Dy)> { (-2, -2), (2, -2), (-2, 2), (2, 2) };
        for (var radius = 2; radius <= 5; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if ((Math.Abs(dx) == radius || Math.Abs(dy) == radius) && !offsets.Contains((dx, dy)))
                    {
                        offsets.Add((dx, dy));
                    }
                }
            }
        }

        return [.. offsets];
    }
}
