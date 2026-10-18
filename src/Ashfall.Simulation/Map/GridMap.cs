using Ashfall.Core;

namespace Ashfall.Simulation.Map;

/// <summary>
/// The playing field: terrain, Cindrite fields and which structure (if any) occupies each cell.
/// Everything here is simulation state and part of the state hash.
/// </summary>
public sealed class GridMap
{
    private const int NoStructure = -1;

    private readonly Terrain[] _terrain;
    private readonly int[] _cindrite;
    private readonly int[] _structure;

    public GridMap(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        _terrain = new Terrain[width * height];
        _cindrite = new int[width * height];
        _structure = new int[width * height];
        Array.Fill(_structure, NoStructure);
    }

    public int Width { get; }

    public int Height { get; }

    public MapBounds Bounds => new(Width, Height);

    /// <summary>Total Cindrite left on the map, in credits.</summary>
    public long TotalCindrite { get; private set; }

    public bool InBounds(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    public Terrain TerrainAt(Cell cell) => _terrain[IndexOf(cell)];

    public void SetTerrain(Cell cell, Terrain terrain) => _terrain[IndexOf(cell)] = terrain;

    public int CindriteAt(Cell cell) => InBounds(cell) ? _cindrite[IndexOf(cell)] : 0;

    public void SetCindrite(Cell cell, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var index = IndexOf(cell);
        TotalCindrite += amount - _cindrite[index];
        _cindrite[index] = amount;
    }

    /// <summary>Removes up to <paramref name="amount"/> Cindrite from a cell and returns what was taken.</summary>
    public int TakeCindrite(Cell cell, int amount)
    {
        var index = IndexOf(cell);
        var taken = _cindrite[index] < amount ? _cindrite[index] : amount;
        _cindrite[index] -= taken;
        TotalCindrite -= taken;
        return taken;
    }

    /// <summary>Entity index of the structure on a cell, or -1.</summary>
    public int StructureAt(Cell cell) => InBounds(cell) ? _structure[IndexOf(cell)] : NoStructure;

    public void SetStructure(Cell origin, int width, int height, int entityIndex)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                _structure[IndexOf(new Cell(origin.X + x, origin.Y + y))] = entityIndex;
            }
        }
    }

    public void ClearStructure(Cell origin, int width, int height) => SetStructure(origin, width, height, NoStructure);

    /// <summary>Ground units can stand on and move through this cell.</summary>
    public bool IsPassable(Cell cell) =>
        InBounds(cell) && _terrain[IndexOf(cell)] == Terrain.Clear && _structure[IndexOf(cell)] == NoStructure;

    /// <summary>A structure footprint may cover this cell: passable and free of Cindrite.</summary>
    public bool IsBuildable(Cell cell) => IsPassable(cell) && _cindrite[IndexOf(cell)] == 0;

    public bool IsPassable(int index) => _terrain[index] == Terrain.Clear && _structure[index] == NoStructure;

    public int IndexOf(Cell cell)
    {
        if (!InBounds(cell))
        {
            throw new ArgumentOutOfRangeException(nameof(cell), cell, "Cell is outside the map.");
        }

        return (cell.Y * Width) + cell.X;
    }

    public Cell CellAt(int index) => new(index % Width, index / Width);

    public void HashInto(ref StateHasher hasher)
    {
        hasher.Add(Width);
        hasher.Add(Height);
        hasher.Add(TotalCindrite);
        for (var i = 0; i < _terrain.Length; i++)
        {
            hasher.Add(((long)_terrain[i] << 32) | (uint)_cindrite[i]);
            hasher.Add(_structure[i]);
        }
    }
}
