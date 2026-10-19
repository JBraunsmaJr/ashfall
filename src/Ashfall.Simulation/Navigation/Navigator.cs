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
    private readonly Stack<int> _fillScratch = new();
    private int[] _region = [];
    private long _regionVersion = -1;

    public Navigator(GridMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _pathfinder = new GridPathfinder(map);
    }

    public PathStore Paths { get; } = new();

    /// <summary>
    /// True when a unit at <paramref name="from"/> could walk to where <see cref="PlanRoute"/> would send it for
    /// <paramref name="to"/> (the nearest free cell when that point is blocked): no route that falls short.
    /// </summary>
    public bool CanReach(FixVec2 from, FixVec2 to) => TryResolveGoal(from, to, out var goal) && CanReach(Cell.Containing(from), goal);

    /// <summary>
    /// The cell a route from <paramref name="from"/> toward <paramref name="to"/> ends in: the cell of
    /// <paramref name="to"/> itself if it is free, otherwise the nearest free cell the unit can walk to (falling back
    /// to the nearest free cell at all, if none within reach is close). So a unit sent at a building whose far side
    /// lies in a sealed pocket goes to the side it can reach.
    /// </summary>
    public bool TryResolveGoal(FixVec2 from, FixVec2 to, out Cell goal)
    {
        goal = Cell.Containing(_map.Bounds.Clamp(to));
        if (_map.IsPassable(goal))
        {
            return true;
        }

        var start = Cell.Containing(from);
        return TryFindNearestPassable(goal, maxRadius: 12, out goal, start) || TryFindNearestPassable(goal, maxRadius: 12, out goal);
    }

    /// <summary>
    /// True when a unit standing in <paramref name="from"/> could walk to <paramref name="to"/>: both cells lie in one
    /// connected stretch of passable ground. A cell that is not passable (a unit pushed against a building, a target
    /// on a footprint) gives no answer either way, so it counts as reachable. The regions are a cache over the map,
    /// rebuilt only when a structure is placed or removed, so asking is cheap; a full A* search that cannot arrive is not.
    /// </summary>
    public bool CanReach(Cell from, Cell to)
    {
        if (!_map.InBounds(from) || !_map.InBounds(to))
        {
            return true;
        }

        if (_regionVersion != _map.StructureVersion || _region.Length != _map.Width * _map.Height)
        {
            LabelRegions();
        }

        var a = _region[_map.IndexOf(from)];
        var b = _region[_map.IndexOf(to)];
        return a < 0 || b < 0 || a == b;
    }

    // Flood-fills passable ground into numbered regions (4-connected: the pathfinder never cuts a blocked corner, so a
    // diagonal step always has an orthogonal way round and adds no connections).
    private void LabelRegions()
    {
        var count = _map.Width * _map.Height;
        if (_region.Length != count)
        {
            _region = new int[count];
        }

        Array.Fill(_region, -1);
        var next = 0;
        for (var start = 0; start < count; start++)
        {
            if (_region[start] >= 0 || !_map.IsPassable(start))
            {
                continue;
            }

            _region[start] = next;
            _fillScratch.Push(start);
            while (_fillScratch.Count > 0)
            {
                var index = _fillScratch.Pop();
                var x = index % _map.Width;
                var y = index / _map.Width;
                Fill(x > 0 ? index - 1 : -1, next);
                Fill(x < _map.Width - 1 ? index + 1 : -1, next);
                Fill(y > 0 ? index - _map.Width : -1, next);
                Fill(y < _map.Height - 1 ? index + _map.Width : -1, next);
            }

            next++;
        }

        _regionVersion = _map.StructureVersion;
    }

    private void Fill(int index, int region)
    {
        if (index >= 0 && _region[index] < 0 && _map.IsPassable(index))
        {
            _region[index] = region;
            _fillScratch.Push(index);
        }
    }

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
        var destination = to;
        if (!TryResolveGoal(from, to, out var goalCell))
        {
            return false;
        }

        if (goalCell != Cell.Containing(to))
        {
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
    public bool TryFindNearestPassable(Cell around, int maxRadius, out Cell found) => TryFindNearestPassable(around, maxRadius, out found, null);

    // As above; with reachableFrom, only cells a unit there could walk to count.
    private bool TryFindNearestPassable(Cell around, int maxRadius, out Cell found, Cell? reachableFrom)
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
                    if (distance < bestDistance && _map.IsPassable(cell) && (reachableFrom is not { } start || CanReach(start, cell)))
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
