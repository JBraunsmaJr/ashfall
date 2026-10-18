using Ashfall.Core;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Vision;

/// <summary>
/// Per-player fog of war: one <see cref="CellVisibility"/> per cell per player. Players on the same team
/// share vision. Simulation state (part of the state hash), so peers agree on what everyone can see and
/// later systems (AI, targeting rules, replays from a player's view) can rely on it.
/// </summary>
public sealed class VisibilityMap
{
    private readonly CellVisibility[][] _cells;
    private readonly int _width;
    private readonly int _height;

    // Disc offsets per sight radius, built on first use. Order is fixed, so stamping is deterministic.
    private readonly Dictionary<int, Cell[]> _discs = [];

    public VisibilityMap(int width, int height, int players)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(players);
        _width = width;
        _height = height;
        _cells = new CellVisibility[players][];
        for (var player = 0; player < players; player++)
        {
            _cells[player] = new CellVisibility[width * height];
        }
    }

    public int Players => _cells.Length;

    public CellVisibility At(int player, Cell cell) =>
        player < 0 || player >= _cells.Length || !InBounds(cell)
            ? CellVisibility.Shroud
            : _cells[player][(cell.Y * _width) + cell.X];

    public bool IsVisible(int player, Cell cell) => At(player, cell) == CellVisibility.Visible;

    public bool IsExplored(int player, Cell cell) => At(player, cell) != CellVisibility.Shroud;

    /// <summary>One player's cells, row by row (y * width + x). For rendering the fog layer.</summary>
    public ReadOnlySpan<CellVisibility> Cells(int player) => _cells[player];

    /// <summary>Turns every visible cell back into fog before vision is stamped again.</summary>
    public void BeginUpdate()
    {
        foreach (var cells in _cells)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                if (cells[i] == CellVisibility.Visible)
                {
                    cells[i] = CellVisibility.Fog;
                }
            }
        }
    }

    /// <summary>Makes every cell within <paramref name="radius"/> of <paramref name="center"/> visible to <paramref name="player"/>.</summary>
    public void Reveal(int player, Cell center, int radius)
    {
        if (player < 0 || player >= _cells.Length || radius < 0)
        {
            return;
        }

        var cells = _cells[player];
        foreach (var offset in Disc(radius))
        {
            var x = center.X + offset.X;
            var y = center.Y + offset.Y;
            if (x >= 0 && y >= 0 && x < _width && y < _height)
            {
                cells[(y * _width) + x] = CellVisibility.Visible;
            }
        }
    }

    public void HashInto(ref StateHasher hasher)
    {
        hasher.Add(_cells.Length);
        foreach (var cells in _cells)
        {
            // Two bits per cell would do; packing eight cells per word keeps hashing cheap.
            ulong word = 0;
            var filled = 0;
            for (var i = 0; i < cells.Length; i++)
            {
                word = (word << 8) | (byte)cells[i];
                if (++filled == 8)
                {
                    hasher.Add(word);
                    word = 0;
                    filled = 0;
                }
            }

            hasher.Add(word);
        }
    }

    private bool InBounds(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < _width && cell.Y < _height;

    // Cells whose centres lie within radius + 0.5 of the centre cell's: rounder than a strict r² test.
    private Cell[] Disc(int radius)
    {
        if (_discs.TryGetValue(radius, out var disc))
        {
            return disc;
        }

        var offsets = new List<Cell>();
        var limit = (radius * radius) + radius;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if ((dx * dx) + (dy * dy) <= limit)
                {
                    offsets.Add(new Cell(dx, dy));
                }
            }
        }

        disc = [.. offsets];
        _discs[radius] = disc;
        return disc;
    }
}
