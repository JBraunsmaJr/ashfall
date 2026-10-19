using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Spatial;

/// <summary>
/// Every mobile thing that can be hit (units and Maw creatures: entities with <see cref="Health"/> but no
/// <see cref="StructureInfo"/>), bucketed by the cell it stands in. Rebuilt once per tick before targeting,
/// so a search for nearby enemies visits a few cells instead of every entity on the map; with thousands of
/// creatures that is the difference between a linear and a quadratic tick.
/// </summary>
/// <remarks>
/// Derived data, not simulation state: it is rebuilt from components and never hashed. Within a cell,
/// entities appear in <see cref="Health"/> store order, so the grid is identical on every peer.
/// Structures are not in the grid: there are few of them and they span several cells.
/// </remarks>
public sealed class SpatialGrid
{
    private readonly int _width;
    private readonly int _height;

    // Counting-sort layout: the entities in cell c are _entries[_cellStart[c] .. _cellStart[c + 1]).
    private readonly int[] _cellStart;
    private readonly int[] _cursor;
    private int[] _entries = new int[256];

    public SpatialGrid(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _width = width;
        _height = height;
        _cellStart = new int[(width * height) + 1];
        _cursor = new int[width * height];
    }

    public int Width => _width;

    public int Height => _height;

    /// <summary>Entities in the grid after the last rebuild.</summary>
    public int Count { get; private set; }

    /// <summary>Re-buckets every mobile entity by its current position.</summary>
    public void Rebuild(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var healths = world.Store<Health>();
        var structures = world.Store<StructureInfo>();
        var garrisoned = world.Store<Garrisoned>();
        var positions = world.Store<Position>();
        var indices = healths.EntityIndices;

        Array.Clear(_cellStart);
        var count = 0;
        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            if (structures.Has(index) || garrisoned.Has(index))
            {
                continue; // structures are looked up on the map; units in a bunker cannot be seen or hit
            }

            _cellStart[CellIndexOf(positions.Get(index).Value) + 1]++;
            count++;
        }

        for (var cell = 0; cell < _cursor.Length; cell++)
        {
            _cellStart[cell + 1] += _cellStart[cell];
            _cursor[cell] = _cellStart[cell];
        }

        if (_entries.Length < count)
        {
            _entries = new int[count * 2];
        }

        for (var i = 0; i < indices.Length; i++)
        {
            var index = indices[i];
            if (structures.Has(index) || garrisoned.Has(index))
            {
                continue;
            }

            _entries[_cursor[CellIndexOf(positions.Get(index).Value)]++] = index;
        }

        Count = count;
    }

    /// <summary>Entity indices standing in cell (<paramref name="x"/>, <paramref name="y"/>); empty outside the map.</summary>
    public ReadOnlySpan<int> At(int x, int y)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
        {
            return [];
        }

        var cell = (y * _width) + x;
        return _entries.AsSpan(_cellStart[cell], _cellStart[cell + 1] - _cellStart[cell]);
    }

    /// <summary>
    /// The inclusive cell range that contains every point within <paramref name="radius"/> of
    /// <paramref name="center"/>, clamped to the map.
    /// </summary>
    public (int MinX, int MinY, int MaxX, int MaxY) CellsAround(FixVec2 center, Fix64 radius) =>
        (Clamp((center.X - radius).FloorToInt(), _width), Clamp((center.Y - radius).FloorToInt(), _height),
         Clamp((center.X + radius).FloorToInt(), _width), Clamp((center.Y + radius).FloorToInt(), _height));

    private int CellIndexOf(FixVec2 position) =>
        (Clamp(position.Y.FloorToInt(), _height) * _width) + Clamp(position.X.FloorToInt(), _width);

    private static int Clamp(int value, int size) => value < 0 ? 0 : value >= size ? size - 1 : value;
}
