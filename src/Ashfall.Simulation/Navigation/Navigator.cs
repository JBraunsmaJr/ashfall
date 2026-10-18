using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Navigation;

/// <summary>
/// Plans routes for moving entities: straight there when the way is clear, otherwise an A* path
/// smoothed into as few straight legs as the terrain allows.
/// </summary>
public sealed class Navigator
{
    // Units are treated as points with this clearance when checking a straight line against obstacles.
    private static readonly Fix64 Clearance = Fix64.FromRatio(3, 10);

    // Samples per cell when walking a straight line.
    private const int SamplesPerCell = 4;

    private readonly GridMap _map;
    private readonly GridPathfinder _pathfinder;
    private readonly List<Cell> _cellScratch = [];
    private readonly List<FixVec2> _pointScratch = [];

    public Navigator(GridMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _pathfinder = new GridPathfinder(map);
    }

    public PathStore Paths { get; } = new();

    /// <summary>
    /// Sends <paramref name="mover"/> from <paramref name="from"/> toward <paramref name="to"/>. If the
    /// destination is blocked, the nearest free cell is used instead; if nothing is reachable, the mover
    /// stops. Returns false when no movement was planned.
    /// </summary>
    public bool PlanRoute(int entityIndex, FixVec2 from, FixVec2 to, ref Mover mover)
    {
        var waypoints = Paths.For(entityIndex);
        waypoints.Clear();
        mover.NextWaypoint = 0;
        mover.HasTarget = false;

        to = _map.Bounds.Clamp(to);
        var goalCell = Cell.Containing(to);
        var destination = to;
        if (!_map.IsPassable(goalCell))
        {
            if (!TryFindNearestPassable(goalCell, maxRadius: 12, out goalCell))
            {
                return false;
            }

            destination = goalCell.Center;
        }

        if (HasClearLine(from, destination))
        {
            waypoints.Add(destination);
        }
        else
        {
            var reached = _pathfinder.FindPath(Cell.Containing(from), goalCell, _cellScratch);
            if (_cellScratch.Count == 0)
            {
                return false;
            }

            _pointScratch.Clear();
            foreach (var cell in _cellScratch)
            {
                _pointScratch.Add(cell.Center);
            }

            if (!reached)
            {
                destination = _pointScratch[^1];
            }

            _pointScratch[^1] = destination;
            Smooth(from, _pointScratch, waypoints);
        }

        mover.Target = destination;
        mover.HasTarget = true;
        return true;
    }

    /// <summary>
    /// True if a unit can walk the straight segment without touching an impassable cell (with clearance).
    /// The map edge counts as open: offsets that fall outside the map are ignored.
    /// </summary>
    public bool HasClearLine(FixVec2 from, FixVec2 to)
    {
        var delta = to - from;
        var lengthSquared = delta.LengthSquared;
        if (lengthSquared == Fix64.Zero)
        {
            return IsClearAround(to);
        }

        var steps = (FixMath.Sqrt(lengthSquared) * SamplesPerCell).CeilingToInt();
        if (steps < 1)
        {
            steps = 1;
        }

        for (var step = 1; step <= steps; step++)
        {
            var point = from + (delta * Fix64.FromRatio(step, steps));
            if (!IsClearAround(point))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds the passable cell closest to <paramref name="around"/> (Euclidean, then scan order),
    /// searching outward ring by ring.
    /// </summary>
    public bool TryFindNearestPassable(Cell around, int maxRadius, out Cell found)
    {
        for (var radius = 0; radius <= maxRadius; radius++)
        {
            var bestDistance = int.MaxValue;
            found = default;
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (dx != -radius && dx != radius && dy != -radius && dy != radius)
                    {
                        continue;
                    }

                    var cell = new Cell(around.X + dx, around.Y + dy);
                    var distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance && _map.IsPassable(cell))
                    {
                        bestDistance = distance;
                        found = cell;
                    }
                }
            }

            if (bestDistance != int.MaxValue)
            {
                return true;
            }
        }

        found = default;
        return false;
    }

    // String pulling: from each anchor, walk forward while the straight line stays clear, and keep the
    // last point that was reachable. Turns a cell-by-cell staircase into a few straight legs.
    private void Smooth(FixVec2 start, List<FixVec2> points, List<FixVec2> output)
    {
        var anchor = start;
        var index = 0;
        while (index < points.Count)
        {
            var farthest = index;
            while (farthest + 1 < points.Count && HasClearLine(anchor, points[farthest + 1]))
            {
                farthest++;
            }

            output.Add(points[farthest]);
            anchor = points[farthest];
            index = farthest + 1;
        }
    }

    private bool IsClearAround(FixVec2 point)
    {
        if (!_map.IsPassable(Cell.Containing(point)))
        {
            return false;
        }

        return IsClearOffset(point.X + Clearance, point.Y)
            && IsClearOffset(point.X - Clearance, point.Y)
            && IsClearOffset(point.X, point.Y + Clearance)
            && IsClearOffset(point.X, point.Y - Clearance);
    }

    private bool IsClearOffset(Fix64 x, Fix64 y)
    {
        var cell = Cell.Containing(new FixVec2(x, y));
        return !_map.InBounds(cell) || _map.IsPassable(cell);
    }
}
