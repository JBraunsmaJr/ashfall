using Ashfall.Rendering;
using Ashfall.Simulation.Map;

namespace Ashfall.UI.World;

/// <summary>Flat colours for terrain seen from above: the minimap and the menus' map previews.</summary>
public static class TerrainColors
{
    public static LinearColor Of(GridMap map, Cell cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.CindriteAt(cell) > 0)
        {
            return Palette.Cindrite;
        }

        var level = map.HeightAt(cell);
        return map.TerrainAt(cell) switch
        {
            Terrain.Rock => Palette.RockTop,
            Terrain.Water => Palette.WaterLight,
            Terrain.Bridge => Palette.BridgeTop,
            Terrain.Trees => Palette.LeavesTop,
            Terrain.Ramp => Palette.Scale(Palette.Ramp, 1.1f + (level * 0.08f)),
            _ => Palette.Scale(Palette.Ground, 1f + (level * 0.12f)),
        };
    }
}
