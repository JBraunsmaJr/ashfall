using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Navigation;

/// <summary>
/// A* over the map grid with 8-way movement (orthogonal cost 10, diagonal 14) and no corner cutting.
/// Fully deterministic: ties break on heuristic, then on insertion order, never on memory layout.
/// Buffers are reused between searches, so steady-state pathfinding does not allocate.
/// </summary>
/// <remarks>
/// M1 searches the full grid. Hierarchical A* and flow fields for large groups (GDD section 9) replace
/// this when maps and army sizes grow.
/// </remarks>
public sealed class GridPathfinder
{
    private const int Straight = 10;
    private const int Diagonal = 14;

    private static readonly (int Dx, int Dy, int Cost)[] Neighbours =
    [
        (1, 0, Straight), (0, 1, Straight), (-1, 0, Straight), (0, -1, Straight),
        (1, 1, Diagonal), (-1, 1, Diagonal), (-1, -1, Diagonal), (1, -1, Diagonal),
    ];

    private readonly GridMap _map;
    private readonly int[] _cost;
    private readonly int[] _cameFrom;
    private readonly int[] _visitStamp;
    private readonly int[] _closedStamp;
    private readonly OpenSet _open;
    private int _stamp;

    public GridPathfinder(GridMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        var cells = map.Width * map.Height;
        _cost = new int[cells];
        _cameFrom = new int[cells];
        _visitStamp = new int[cells];
        _closedStamp = new int[cells];
        _open = new OpenSet(cells);
    }

    /// <summary>
    /// Finds a path from <paramref name="start"/> to <paramref name="goal"/>. <paramref name="path"/> receives
    /// the cells to walk through, excluding the start. If the goal is unreachable, the path leads to the
    /// reachable cell closest to it and the method returns false.
    /// </summary>
    public bool FindPath(Cell start, Cell goal, List<Cell> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        if (!_map.InBounds(start) || !_map.InBounds(goal))
        {
            return false;
        }

        if (start == goal)
        {
            return true;
        }

        NextStamp();
        var startIndex = _map.IndexOf(start);
        var goalIndex = _map.IndexOf(goal);
        var bestIndex = startIndex;
        var bestHeuristic = Heuristic(start, goal);

        Visit(startIndex, cost: 0, cameFrom: -1);
        _open.Push(startIndex, bestHeuristic, bestHeuristic);

        while (_open.Count > 0)
        {
            var current = _open.Pop();
            if (_closedStamp[current] == _stamp)
            {
                continue;
            }

            _closedStamp[current] = _stamp;
            if (current == goalIndex)
            {
                bestIndex = current;
                break;
            }

            var cell = _map.CellAt(current);
            var currentHeuristic = Heuristic(cell, goal);
            if (currentHeuristic < bestHeuristic)
            {
                bestHeuristic = currentHeuristic;
                bestIndex = current;
            }

            foreach (var (dx, dy, stepCost) in Neighbours)
            {
                var next = new Cell(cell.X + dx, cell.Y + dy);
                if (!_map.IsPassable(next))
                {
                    continue;
                }

                // No squeezing diagonally between two blocked cells or around a structure's corner.
                if (dx != 0 && dy != 0
                    && (!_map.IsPassable(new Cell(cell.X + dx, cell.Y)) || !_map.IsPassable(new Cell(cell.X, cell.Y + dy))))
                {
                    continue;
                }

                var nextIndex = _map.IndexOf(next);
                if (_closedStamp[nextIndex] == _stamp)
                {
                    continue;
                }

                var nextCost = _cost[current] + stepCost;
                if (_visitStamp[nextIndex] == _stamp && nextCost >= _cost[nextIndex])
                {
                    continue;
                }

                Visit(nextIndex, nextCost, current);
                var heuristic = Heuristic(next, goal);
                _open.Push(nextIndex, nextCost + heuristic, heuristic);
            }
        }

        _open.Clear();

        for (var index = bestIndex; index != startIndex && index >= 0; index = _cameFrom[index])
        {
            path.Add(_map.CellAt(index));
        }

        path.Reverse();
        return bestIndex == goalIndex;
    }

    // Octile distance: exact cost on an open grid, so the search never overestimates.
    private static int Heuristic(Cell from, Cell to)
    {
        var dx = from.X > to.X ? from.X - to.X : to.X - from.X;
        var dy = from.Y > to.Y ? from.Y - to.Y : to.Y - from.Y;
        var low = dx < dy ? dx : dy;
        var high = dx < dy ? dy : dx;
        return (Straight * high) + ((Diagonal - Straight) * low);
    }

    private void Visit(int index, int cost, int cameFrom)
    {
        _visitStamp[index] = _stamp;
        _cost[index] = cost;
        _cameFrom[index] = cameFrom;
    }

    private void NextStamp()
    {
        _stamp++;
        if (_stamp == int.MaxValue)
        {
            Array.Clear(_visitStamp);
            Array.Clear(_closedStamp);
            _stamp = 1;
        }
    }

    /// <summary>Binary min-heap ordered by (f, h, insertion order).</summary>
    private sealed class OpenSet(int capacity)
    {
        private Entry[] _entries = new Entry[capacity + 1];
        private int _sequence;

        public int Count { get; private set; }

        public void Push(int index, int f, int h)
        {
            if (Count == _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length * 2);
            }

            var entry = new Entry(f, h, _sequence++, index);
            var position = Count++;
            while (position > 0)
            {
                var parent = (position - 1) / 2;
                if (!Less(entry, _entries[parent]))
                {
                    break;
                }

                _entries[position] = _entries[parent];
                position = parent;
            }

            _entries[position] = entry;
        }

        public int Pop()
        {
            var top = _entries[0].Index;
            var last = _entries[--Count];
            var position = 0;
            while (true)
            {
                var child = (position * 2) + 1;
                if (child >= Count)
                {
                    break;
                }

                if (child + 1 < Count && Less(_entries[child + 1], _entries[child]))
                {
                    child++;
                }

                if (!Less(_entries[child], last))
                {
                    break;
                }

                _entries[position] = _entries[child];
                position = child;
            }

            _entries[position] = last;
            return top;
        }

        public void Clear()
        {
            Count = 0;
            _sequence = 0;
        }

        private static bool Less(in Entry a, in Entry b) =>
            a.F != b.F ? a.F < b.F : a.H != b.H ? a.H < b.H : a.Sequence < b.Sequence;

        private readonly record struct Entry(int F, int H, int Sequence, int Index);
    }
}
