using Ashfall.Content;
using Ashfall.Simulation;
using Ashfall.Simulation.Map;

namespace Ashfall.Session.Maps;

/// <summary>
/// Seeds a Purge map with the Maw (GDD section 2): Spawning Pits anywhere far enough from the players' bases, beside
/// the fissure of a Cindrite field or out on open ground. A pit on open ground brings its own blight: a small field
/// rising from a fissure beside it, so every nest has something to feed on that keeps regrowing until the players
/// take it. Sites are chosen far from the bases and spread out from each other; the players' own fields are never
/// infested.
/// </summary>
public static class PurgeNests
{
    /// <summary>No nest is closer than this to a player's Construction Yard, in cells.</summary>
    public const int MinimumDistance = 20;

    // Open-ground sites are tried on a grid this many cells apart.
    private const int OpenGrid = 5;

    // The blight a nest on open ground brings: its radius and density.
    private const int BlightRadius = 2;
    private const int BlightDensity = 350;

    // Where the nest's 3x3 footprint centre may sit relative to its fissure: off a corner first (so the vent stays
    // open beside the nest, as in the swarm fortress), then anywhere close.
    private static readonly (int Dx, int Dy)[] Offsets = BuildOffsets();

    // Where a blight's fissure may go relative to its nest's centre: beside it on one side or another.
    private static readonly (int Dx, int Dy)[] BlightOffsets = [(4, 0), (0, 4), (-4, 0), (0, -4), (4, 4), (-4, 4), (4, -4), (-4, -4)];

    /// <summary>
    /// Places up to <paramref name="count"/> Spawning Pits and returns how many were placed (fewer when the map has
    /// too little room far from the bases). The choice depends only on the map and the starts, so every peer agrees.
    /// </summary>
    public static int Place(GameSimulation simulation, IReadOnlyList<Cell> playerStarts, int count) =>
        Place(simulation, playerStarts, count, simulation?.Content.StructureIndex(MawContent.SpawningPit) ?? throw new ArgumentNullException(nameof(simulation)));

    /// <summary>
    /// Places up to <paramref name="count"/> nests of <paramref name="nestType"/> (a 3x3 nest structure: a campaign
    /// mission may start at a higher tier) the same way, and returns how many were placed.
    /// </summary>
    public static int Place(GameSimulation simulation, IReadOnlyList<Cell> playerStarts, int count, int nestType)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(playerStarts);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var map = simulation.Map;
        var pit = nestType;
        var footprint = simulation.Content.Structure(pit);
        var far = MinimumDistance * MinimumDistance;

        // Every field's fissure, then open ground on a coarse grid, as long as it is far enough from every base.
        var candidates = new List<(Cell Anchor, bool Fissure)>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = new Cell(x, y);
                if (map.IsFissure(cell) && Nearest(cell, playerStarts) >= far)
                {
                    candidates.Add((cell, true));
                }
            }
        }

        for (var y = 3; y < map.Height - 3; y += OpenGrid)
        {
            for (var x = 3; x < map.Width - 3; x += OpenGrid)
            {
                var cell = new Cell(x, y);
                if (Nearest(cell, playerStarts) >= far && Fits(map, new Cell(x - 1, y - 1), footprint.Width, footprint.Height))
                {
                    candidates.Add((cell, false));
                }
            }
        }

        // Farthest-point order: each nest goes where it is farthest from the bases and from the nests before it
        // (ties: the earlier candidate), so a handful of nests covers the map instead of clumping.
        var taken = new List<Cell>(playerStarts);
        var placed = 0;
        while (placed < count && candidates.Count > 0)
        {
            var best = 0;
            var bestDistance = -1;
            for (var i = 0; i < candidates.Count; i++)
            {
                var distance = Nearest(candidates[i].Anchor, taken);
                if (distance > bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            var (anchor, fissure) = candidates[best];
            candidates.RemoveAt(best);
            Cell origin;
            if (fissure)
            {
                if (!TryFindOrigin(map, anchor, footprint.Width, footprint.Height, out origin))
                {
                    continue;
                }
            }
            else
            {
                origin = new Cell(anchor.X - 1, anchor.Y - 1);
                if (!Fits(map, origin, footprint.Width, footprint.Height))
                {
                    continue; // an earlier nest took the ground
                }
            }

            for (var y = 0; y < footprint.Height; y++)
            {
                for (var x = 0; x < footprint.Width; x++)
                {
                    map.SetCindrite(new Cell(origin.X + x, origin.Y + y), 0); // nothing grows under the nest
                }
            }

            simulation.PlaceNest(pit, origin);
            if (!fissure)
            {
                SeedBlight(map, anchor);
            }

            taken.Add(anchor);
            placed++;
        }

        return placed;
    }

    // A small field rising from a fissure beside a nest on open ground, on clear unbuilt cells only.
    private static void SeedBlight(GridMap map, Cell nest)
    {
        foreach (var (dx, dy) in BlightOffsets)
        {
            var centre = new Cell(nest.X + dx, nest.Y + dy);
            if (!map.InBounds(centre) || map.TerrainAt(centre) != Terrain.Clear || map.StructureAt(centre) >= 0)
            {
                continue;
            }

            for (var y = -BlightRadius; y <= BlightRadius; y++)
            {
                for (var x = -BlightRadius; x <= BlightRadius; x++)
                {
                    var distance = Math.Abs(x) + Math.Abs(y);
                    var cell = new Cell(centre.X + x, centre.Y + y);
                    if (distance <= BlightRadius && map.InBounds(cell) && map.TerrainAt(cell) == Terrain.Clear
                        && map.StructureAt(cell) < 0 && map.CindriteAt(cell) == 0)
                    {
                        map.SetCindrite(cell, BlightDensity + ((BlightRadius - distance) * 50));
                    }
                }
            }

            map.SetFissure(centre, BlightDensity + (BlightRadius * 50));
            return;
        }
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
