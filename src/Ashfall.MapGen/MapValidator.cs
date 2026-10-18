using Ashfall.Simulation.Map;

namespace Ashfall.MapGen;

/// <summary>
/// Hard checks and fairness measurements for a map (GDD section 6, stage 7). Works on any grid with
/// start positions, hand-made or generated:
/// <list type="bullet">
/// <item>every Construction Yard footprint is buildable, and every start can reach every other by ground;</item>
/// <item>at least <see cref="MinReachableResources"/> permille of the Cindrite can be reached;</item>
/// <item>each start has at least <see cref="MinBuildableArea"/> buildable cells near it;</item>
/// <item>nearest-enemy path lengths and home resources agree across starts within <see cref="MaxRatio"/> permille.</item>
/// </list>
/// </summary>
public static class MapValidator
{
    public const int YardSize = 3;

    /// <summary>Buildable cells required within <see cref="BaseRadius"/> of each start.</summary>
    public const int MinBuildableArea = 140;

    public const int BaseRadius = 9;

    /// <summary>Path distance (cells) within which Cindrite counts as a start's home resources.</summary>
    public const int HomeReach = 18;

    /// <summary>Largest over smallest, in permille, allowed for paths and home resources (±5%).</summary>
    public const int MaxRatio = 1050;

    public const int MinReachableResources = 950;

    /// <summary>Starts closer than this by ground (cells) make for an unplayable rush map.</summary>
    public const int MinEnemyPath = 24;

    public static MapReport Validate(GridMap map, IReadOnlyList<Cell> startOrigins, int players, int symmetryOrder)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(startOrigins);
        var failures = new List<string>();
        var starts = startOrigins.Take(players).ToArray();

        foreach (var (origin, player) in starts.Select((origin, player) => (origin, player)))
        {
            for (var y = 0; y < YardSize; y++)
            {
                for (var x = 0; x < YardSize; x++)
                {
                    if (!map.IsBuildable(new Cell(origin.X + x, origin.Y + y)))
                    {
                        failures.Add($"start {player + 1}: the Construction Yard footprint is blocked");
                        y = YardSize;
                        break;
                    }
                }
            }
        }

        var distances = starts.Select(origin => Distances(map, origin)).ToArray();
        var paths = new int[starts.Length];
        for (var player = 0; player < starts.Length; player++)
        {
            var nearest = int.MaxValue;
            for (var other = 0; other < starts.Length; other++)
            {
                if (other != player)
                {
                    var distance = DistanceToFootprint(distances[player], map, starts[other]);
                    if (distance < 0)
                    {
                        failures.Add($"start {player + 1} cannot reach start {other + 1} by ground");
                    }
                    else if (distance < nearest)
                    {
                        nearest = distance;
                    }
                }
            }

            paths[player] = nearest == int.MaxValue ? 0 : nearest;
            if (paths[player] > 0 && paths[player] < MinEnemyPath)
            {
                failures.Add($"start {player + 1} is only {paths[player]} cells from an enemy");
            }
        }

        // Resources: reachable share overall, and the home share per start.
        long total = 0;
        long reachable = 0;
        var home = new long[starts.Length];
        var area = new int[starts.Length];
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = new Cell(x, y);
                var index = map.IndexOf(cell);
                var amount = map.CindriteAt(cell);
                if (amount > 0)
                {
                    total += amount;
                    if (distances[0][index] >= 0)
                    {
                        reachable += amount;
                    }

                    for (var player = 0; player < starts.Length; player++)
                    {
                        if (distances[player][index] is >= 0 and <= HomeReach)
                        {
                            home[player] += amount;
                        }
                    }
                }

                for (var player = 0; player < starts.Length; player++)
                {
                    var centre = new Cell(starts[player].X + 1, starts[player].Y + 1);
                    if (Cell.ChebyshevDistance(cell, centre) <= BaseRadius && map.IsBuildable(cell) && distances[player][index] >= 0)
                    {
                        area[player]++;
                    }
                }
            }
        }

        var reachablePermille = total == 0 ? 0 : (int)(reachable * 1000 / total);
        if (reachablePermille < MinReachableResources)
        {
            failures.Add($"only {reachablePermille / 10}% of the Cindrite can be reached");
        }

        for (var player = 0; player < starts.Length; player++)
        {
            if (area[player] < MinBuildableArea)
            {
                failures.Add($"start {player + 1} has only {area[player]} buildable cells nearby");
            }

            if (home[player] == 0)
            {
                failures.Add($"start {player + 1} has no Cindrite within reach");
            }
        }

        var pathRatio = Ratio(paths.Select(p => (long)p).ToArray());
        var resourceRatio = Ratio(home);
        if (pathRatio > MaxRatio)
        {
            failures.Add($"paths to the nearest enemy differ by {(pathRatio - 1000) / 10}%");
        }

        if (resourceRatio > MaxRatio)
        {
            failures.Add($"home resources differ by {(resourceRatio - 1000) / 10}%");
        }

        // 100 for identical starts; each percent of imbalance costs two points, failures cost the rest.
        var score = 100 - ((pathRatio - 1000) / 5) - ((resourceRatio - 1000) / 5) - (failures.Count * 25);
        _ = symmetryOrder;
        return new MapReport
        {
            Failures = failures,
            FairnessScore = score < 0 ? 0 : score > 100 ? 100 : score,
            PathToNearestEnemy = paths,
            HomeResources = home,
            BuildableArea = area,
            PathRatio = pathRatio,
            ResourceRatio = resourceRatio,
            ReachableResources = reachablePermille,
            TotalResources = total,
        };
    }

    /// <summary>Ground distance (8-way steps, no corner cutting) from a yard footprint's neighbours to every cell; -1 if unreachable.</summary>
    public static int[] Distances(GridMap map, Cell yardOrigin)
    {
        ArgumentNullException.ThrowIfNull(map);
        var distance = new int[map.Width * map.Height];
        Array.Fill(distance, -1);
        var queue = new Queue<int>();
        for (var y = -1; y <= YardSize; y++)
        {
            for (var x = -1; x <= YardSize; x++)
            {
                var cell = new Cell(yardOrigin.X + x, yardOrigin.Y + y);
                var inside = x >= 0 && y >= 0 && x < YardSize && y < YardSize;
                if (map.InBounds(cell) && (inside || map.IsPassable(cell)))
                {
                    distance[map.IndexOf(cell)] = 0;
                    queue.Enqueue(map.IndexOf(cell));
                }
            }
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var cell = map.CellAt(index);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    var next = new Cell(cell.X + dx, cell.Y + dy);
                    if (!map.InBounds(next) || !map.IsPassable(next))
                    {
                        continue;
                    }

                    // Diagonal steps need both side cells open, as the pathfinder does.
                    if (dx != 0 && dy != 0 && (!map.IsPassable(new Cell(cell.X + dx, cell.Y)) || !map.IsPassable(new Cell(cell.X, cell.Y + dy))))
                    {
                        continue;
                    }

                    var nextIndex = map.IndexOf(next);
                    if (distance[nextIndex] < 0)
                    {
                        distance[nextIndex] = distance[index] + 1;
                        queue.Enqueue(nextIndex);
                    }
                }
            }
        }

        return distance;
    }

    private static int DistanceToFootprint(int[] distance, GridMap map, Cell origin)
    {
        var best = -1;
        for (var y = 0; y < YardSize; y++)
        {
            for (var x = 0; x < YardSize; x++)
            {
                var cell = new Cell(origin.X + x, origin.Y + y);
                if (map.InBounds(cell))
                {
                    var d = distance[map.IndexOf(cell)];
                    if (d >= 0 && (best < 0 || d < best))
                    {
                        best = d;
                    }
                }
            }
        }

        return best;
    }

    private static int Ratio(long[] values)
    {
        if (values.Length == 0)
        {
            return 1000;
        }

        var min = values.Min();
        var max = values.Max();
        return min <= 0 ? (max <= 0 ? 1000 : int.MaxValue / 2) : (int)(max * 1000 / min);
    }
}
