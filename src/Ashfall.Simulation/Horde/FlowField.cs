using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Horde;

/// <summary>
/// The swarm's map of the way in (GDD section 12, "the swarm path"): for every passable cell, the cost of the
/// cheapest walk to a cell next to a player structure. Creatures read the field at their cell and step to the
/// cheapest neighbour, so thousands of them need no individual pathfinding.
/// </summary>
/// <remarks>
/// <para>Built by Dijkstra from every cell occupied by a player-owned structure (cost 0), spreading over
/// passable cells with the same 8-way step costs (10 straight, 14 diagonal) and corner rule as unit
/// pathfinding. Step costs are small integers, so the queue is a ring of buckets (Dial's algorithm) and a
/// rebuild is linear in the map's size. Shortest-path costs are unique whatever order ties are taken in, so
/// the field is identical on every peer.</para>
/// <para>Derived data, not simulation state: the field is always a pure function of the current terrain and
/// structure layout (it is rebuilt before use whenever the layout has changed), so it is never hashed and a
/// restored simulation rebuilds exactly the same field.</para>
/// </remarks>
public sealed class FlowField
{
    /// <summary>Cost of a cell from which no player structure can be reached.</summary>
    public const int Unreachable = int.MaxValue;

    /// <summary>Step costs: one straight cell is 10, one diagonal 14.</summary>
    public const int StraightCost = 10;

    public const int DiagonalCost = 14;

    private static readonly (int Dx, int Dy, int Cost)[] Neighbours =
    [
        (1, 0, StraightCost), (0, 1, StraightCost), (-1, 0, StraightCost), (0, -1, StraightCost),
        (1, 1, DiagonalCost), (-1, 1, DiagonalCost), (-1, -1, DiagonalCost), (1, -1, DiagonalCost),
    ];

    private readonly GridMap _map;
    private readonly int[] _cost;
    private readonly bool[] _source;

    // Dial's algorithm: bucket (cost % BucketCount) holds cells queued at that cost. A step costs at most
    // DiagonalCost, so every queued cost is within one ring of the cost being processed.
    private const int BucketCount = DiagonalCost + 1;
    private readonly List<int>[] _buckets;

    public FlowField(GridMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _cost = new int[map.Width * map.Height];
        _source = new bool[map.Width * map.Height];
        Array.Fill(_cost, Unreachable);
        _buckets = new List<int>[BucketCount];
        for (var i = 0; i < BucketCount; i++)
        {
            _buckets[i] = [];
        }
    }

    /// <summary>The <see cref="GridMap.StructureVersion"/> the field was last built for, or -1 if never built.</summary>
    public long BuiltForVersion { get; private set; } = -1;

    /// <summary>Number of cells holding a player structure in the last build: 0 means there is nothing to attack.</summary>
    public int SourceCells { get; private set; }

    /// <summary>Walking cost from <paramref name="cell"/> to the nearest player structure, or <see cref="Unreachable"/>.</summary>
    public int CostAt(Cell cell) => _map.InBounds(cell) ? _cost[_map.IndexOf(cell)] : Unreachable;

    /// <summary>True if <paramref name="cell"/> held a player structure when the field was built.</summary>
    public bool IsSource(Cell cell) => _map.InBounds(cell) && _source[_map.IndexOf(cell)];

    /// <summary>
    /// Recomputes the field toward every structure owned by a player of <paramref name="simulation"/>, or only by
    /// <paramref name="player"/>, and with <paramref name="refineriesOnly"/> only toward that player's refineries.
    /// </summary>
    public void Rebuild(GameSimulation simulation, int player = -1, bool refineriesOnly = false)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var owners = simulation.World.Store<Owner>();
        var infos = simulation.World.Store<StructureInfo>();
        Array.Fill(_cost, Unreachable);
        Array.Clear(_source);
        foreach (var bucket in _buckets)
        {
            bucket.Clear();
        }

        SourceCells = 0;
        var queued = 0;
        for (var index = 0; index < _cost.Length; index++)
        {
            var structure = _map.StructureAt(_map.CellAt(index));
            if (structure < 0 || !owners.Has(structure) || !simulation.IsPlayer(owners.Get(structure).PlayerId)
                || (player >= 0 && owners.Get(structure).PlayerId != player)
                || (refineriesOnly && simulation.Content.Structure(infos.Get(structure).StructureType).Role != Content.StructureRole.Refinery))
            {
                continue;
            }

            _source[index] = true;
            _cost[index] = 0;
            SourceCells++;
            _buckets[0].Add(index);
            queued++;
        }

        for (var cost = 0; queued > 0; cost++)
        {
            var bucket = _buckets[cost % BucketCount];
            for (var i = 0; i < bucket.Count; i++)
            {
                var current = bucket[i];
                queued--;
                if (_cost[current] != cost)
                {
                    continue; // reached more cheaply after it was queued
                }

                var cell = _map.CellAt(current);
                foreach (var (dx, dy, stepCost) in Neighbours)
                {
                    var next = new Cell(cell.X + dx, cell.Y + dy);
                    if (!_map.IsPassable(next) || !CanStep(next, cell))
                    {
                        continue;
                    }

                    var nextIndex = _map.IndexOf(next);
                    var nextCost = cost + stepCost;
                    if (nextCost < _cost[nextIndex])
                    {
                        _cost[nextIndex] = nextCost;
                        _buckets[nextCost % BucketCount].Add(nextIndex);
                        queued++;
                    }
                }
            }

            bucket.Clear();
        }

        BuiltForVersion = _map.StructureVersion;
    }

    /// <summary>
    /// Whether a creature may step from <paramref name="from"/> to the neighbouring <paramref name="to"/> (or reach
    /// across to attack it): straight steps always; diagonal steps only when both cells beside the diagonal are
    /// passable (no squeezing past a corner), except onto a structure cell, which a creature attacks rather than enters.
    /// </summary>
    public bool CanStep(Cell from, Cell to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx == 0 || dy == 0 || _map.StructureAt(to) >= 0)
        {
            return true;
        }

        return _map.IsPassable(new Cell(from.X + dx, from.Y)) && _map.IsPassable(new Cell(from.X, from.Y + dy));
    }
}
