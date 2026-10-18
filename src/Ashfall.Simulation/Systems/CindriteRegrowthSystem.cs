﻿using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Cindrite fields grow back (GDD section 2): every <see cref="Content.EconomyRules.CindriteRegrowIntervalTicks"/>
/// ticks, each field cell below the density it was laid down with regains
/// <see cref="Content.EconomyRules.CindriteRegrowAmount"/>, as long as it or a straight neighbour still holds some
/// Cindrite. A cell harvested to nothing comes back only when the field around it is alive, so a field picked
/// completely clean (or purged) stays dead. Only in matches created with regrowth on.
/// </summary>
/// <remarks>
/// Which cells regrow is decided from the field as it stands before any cell grows, then all of them grow, so the
/// result does not depend on the order cells are visited.
/// </remarks>
public sealed class CindriteRegrowthSystem : ISystem
{
    private static readonly (int Dx, int Dy)[] Neighbours = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private readonly List<Cell> _growing = [];
    private List<Cell>? _fieldCells;

    public void Update(in TickContext context)
    {
        var rules = context.Content.Economy;
        if (!context.Simulation.CindriteRegrowth || context.Tick == 0 || context.Tick % rules.CindriteRegrowIntervalTicks != 0)
        {
            return;
        }

        var map = context.Map;
        _fieldCells ??= FieldCells(map); // caps are fixed once the map is built
        _growing.Clear();
        foreach (var cell in _fieldCells)
        {
            if (map.CindriteAt(cell) < map.CindriteCapAt(cell) && IsAlive(map, cell))
            {
                _growing.Add(cell);
            }
        }

        foreach (var cell in _growing)
        {
            map.RegrowCindrite(cell, rules.CindriteRegrowAmount);
        }
    }

    private static bool IsAlive(GridMap map, Cell cell)
    {
        if (map.CindriteAt(cell) > 0)
        {
            return true;
        }

        foreach (var (dx, dy) in Neighbours)
        {
            if (map.CindriteAt(new Cell(cell.X + dx, cell.Y + dy)) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static List<Cell> FieldCells(GridMap map)
    {
        var cells = new List<Cell>();
        for (var index = 0; index < map.Width * map.Height; index++)
        {
            var cell = map.CellAt(index);
            if (map.CindriteCapAt(cell) > 0)
            {
                cells.Add(cell);
            }
        }

        return cells;
    }
}
