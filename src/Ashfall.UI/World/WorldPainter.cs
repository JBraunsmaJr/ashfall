using System.Numerics;
using Ashfall.Content;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.UI.Drawing;

namespace Ashfall.UI.World;

/// <summary>
/// Turns the simulation into world geometry: terrain diamonds, Cindrite, rock, structures, units,
/// shadows and selection marks. Placeholder shapes stand in for the cooked sprites (GDD section 5)
/// but already use the real lighting path: every face has a normal, structures carry lights.
/// </summary>
internal sealed class WorldPainter(WorldBatch batch, FogField fog, MotionTracker motion)
{
    private const float GroundEpsilon = 0.01f;

    /// <summary>World height of one terrain level.</summary>
    public const float LevelHeight = 0.32f;

    /// <summary>Ground height (world z) under a world point: its cell's level, halfway on ramps, sea level on water.</summary>
    public static float GroundHeight(GridMap map, float x, float y)
    {
        var cell = new Cell((int)MathF.Floor(x), (int)MathF.Floor(y));
        if (!map.InBounds(cell))
        {
            return 0f;
        }

        var level = map.HeightAt(cell);
        return map.TerrainAt(cell) switch
        {
            Terrain.Water or Terrain.Bridge => 0f,
            Terrain.Ramp => Math.Max(0f, (level - 0.5f) * LevelHeight),
            _ => level * LevelHeight,
        };
    }

    // The drawn top of a cell's ground: water sits a little below land.
    private static float SurfaceHeight(GridMap map, Cell cell) =>
        map.TerrainAt(cell) is Terrain.Water or Terrain.Bridge ? -0.06f : GroundHeight(map, cell.X, cell.Y);

    public void PaintTerrain(GameSimulation simulation, IsoCamera camera, double time)
    {
        var map = simulation.Map;
        var (minX, minY, maxX, maxY) = VisibleCells(camera, map);

        // Ground is one layer under everything, so it needs no sorting: one object for all of it.
        batch.Begin(float.MinValue);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var cell = new Cell(x, y);
                var top = SurfaceHeight(map, cell);
                LinearColor color;
                switch (map.TerrainAt(cell))
                {
                    case Terrain.Water:
                    case Terrain.Bridge:
                        // A gentle shimmer: each cell's brightness drifts on its own phase.
                        var shimmer = 0.5f + (0.5f * MathF.Sin((float)(time * 1.3) + (Hash(x, y) % 628 / 100f)));
                        color = Palette.Mix(Palette.Water, Palette.WaterLight, shimmer * 0.25f);
                        batch.Flat(x, y, 1, 1, top, color, emissive: 0.08f);
                        break;
                    case Terrain.Ramp:
                        color = Variation(Palette.Ramp, x, y, 0.06f);
                        batch.Flat(x, y, 1, 1, top, color);
                        break;
                    default:
                        var cindrite = map.CindriteAt(cell);
                        color = Palette.Scale(Variation(cindrite > 0 ? Palette.CindriteGround : Palette.Ground, x, y, 0.07f), 1f + (map.HeightAt(cell) * 0.05f));
                        batch.Flat(x, y, 1, 1, top, color);
                        break;
                }

                // Skirts down to lower neighbours on the two sides the camera sees, so steps have faces, not cracks.
                var side = Palette.Scale(color, 0.6f);
                var east = new Cell(x + 1, y);
                if (map.InBounds(east) && SurfaceHeight(map, east) < top)
                {
                    var low = SurfaceHeight(map, east);
                    batch.Quad(new Vector3(x + 1, y, low), new Vector3(x + 1, y + 1, low), new Vector3(x + 1, y + 1, top), new Vector3(x + 1, y, top), Vector3.UnitX, side);
                }

                var south = new Cell(x, y + 1);
                if (map.InBounds(south) && SurfaceHeight(map, south) < top)
                {
                    var low = SurfaceHeight(map, south);
                    batch.Quad(new Vector3(x, y + 1, low), new Vector3(x + 1, y + 1, low), new Vector3(x + 1, y + 1, top), new Vector3(x, y + 1, top), Vector3.UnitY, side);
                }
            }
        }

        batch.End();

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var cell = new Cell(x, y);
                var top = map.HeightAt(cell) * LevelHeight;
                switch (map.TerrainAt(cell))
                {
                    case Terrain.Rock:
                        // Cliffs and outcrops: rock from the lowest ground up past the cell's level.
                        batch.Begin(x + y + 1);
                        var height = top + 0.3f + (Hash(x, y) % 4 * 0.06f);
                        batch.Box(x, y, 1, 1, 0, height, Variation(Palette.Rock, x, y, 0.08f), Variation(Palette.RockTop, x, y, 0.08f));
                        batch.End();
                        continue;

                    case Terrain.Trees:
                        PaintTree(x, y, top);
                        continue;

                    case Terrain.Bridge:
                        batch.Begin(x + y + 0.5f);
                        batch.Box(x - 0.02f, y - 0.02f, 1.04f, 1.04f, -0.06f, 0.08f, Palette.Bridge, Variation(Palette.BridgeTop, x, y, 0.08f));
                        batch.End();
                        continue;
                }

                var cindrite = map.CindriteAt(cell);
                if (cindrite > 0 && fog.At(x, y) > 0f)
                {
                    PaintCrystals(x, y, cindrite, top);
                }
            }
        }
    }

    private void PaintTree(int x, int y, float ground)
    {
        // One or two trees per cell, each a trunk under a stacked canopy, jittered so woods look natural.
        var seed = Hash(x, y);
        batch.Begin(x + y + 1, ground);
        var count = 1 + (seed & 1);
        for (var i = 0; i < count; i++)
        {
            var ox = 0.25f + (((seed >> (4 + (i * 6))) & 0xF) / 15f * 0.5f);
            var oy = 0.25f + (((seed >> (8 + (i * 6))) & 0xF) / 15f * 0.5f);
            var size = 0.22f + (((seed >> (12 + i)) & 0x3) * 0.04f);
            var height = 0.5f + (((seed >> (16 + i)) & 0x3) * 0.08f);
            batch.Disc(new Vector2(x + ox + 0.08f, y + oy - 0.04f), size * 1.2f, GroundEpsilon, Palette.Shadow);
            batch.Box(x + ox - 0.04f, y + oy - 0.04f, 0.08f, 0.08f, 0, height * 0.4f, Palette.Trunk, Palette.Trunk);
            batch.Box(x + ox - size, y + oy - size, size * 2, size * 2, height * 0.35f, height, Palette.Leaves, Palette.LeavesTop);
            batch.Box(x + ox - (size * 0.6f), y + oy - (size * 0.6f), size * 1.2f, size * 1.2f, height, height + 0.18f, Palette.Leaves, Palette.LeavesTop);
        }

        batch.End();
    }

    public void PaintStructures(GameSimulation simulation, int localPlayer, ISet<Entity> selected, FrameScene scene)
    {
        var world = simulation.World;
        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            var entity = world.EntityAt(index);
            var info = world.Store<StructureInfo>().Get(index);
            var owner = world.Store<Owner>().Get(index).PlayerId;
            var definition = simulation.Content.Structure(info.StructureType);
            var centerX = info.Origin.X + (definition.Width / 2f);
            var centerY = info.Origin.Y + (definition.Height / 2f);

            // Enemy structures show once explored (as last seen); shrouded ones stay hidden.
            if (owner != localPlayer && fog.Sample(centerX, centerY) <= 0.01f)
            {
                continue;
            }

            var progress = info.IsComplete ? 1f : 1f - (info.BuildRemaining / (float)definition.BuildTicks);
            batch.Begin(info.Origin.X + definition.Width + info.Origin.Y + definition.Height - 1, GroundHeight(simulation.Map, centerX, centerY));
            PaintStructure(definition, info.Origin, owner, progress, scene);
            if (selected.Contains(entity))
            {
                batch.Outline(info.Origin.X - 0.05f, info.Origin.Y - 0.05f, definition.Width + 0.1f, definition.Height + 0.1f, 0.08f, GroundEpsilon * 3, Palette.Selection);
            }

            batch.End();
        }
    }

    public void PaintUnits(GameSimulation simulation, int localPlayer, ISet<Entity> selected, float alpha)
    {
        var world = simulation.World;
        foreach (var index in world.Store<UnitInfo>().EntityIndices)
        {
            var entity = world.EntityAt(index);
            var owner = world.Store<Owner>().Get(index).PlayerId;
            var fallback = MotionTracker.ToVector(world.Store<Position>().Get(index).Value);
            var position = motion.PositionOf(entity, fallback, alpha);
            if (owner != localPlayer && fog.Sample(position.X, position.Y) < 0.75f)
            {
                continue; // enemies are only seen inside current vision
            }

            var definition = simulation.Content.Unit(world.Store<UnitInfo>().Get(index).UnitType);
            var heading = motion.HeadingOf(entity);
            batch.Begin(position.X + position.Y, GroundHeight(simulation.Map, position.X, position.Y));
            if (selected.Contains(entity))
            {
                batch.Disc(position, definition.IsHarvester ? 0.62f : definition.Id == BulwarkContent.Warden ? 0.52f : 0.3f, GroundEpsilon * 2, Palette.Selection, emissive: 1f, shape: QuadShape.Ring);
            }

            var cargo = world.Store<Harvester>().Has(index)
                ? world.Store<Harvester>().Get(index).Cargo / (float)Math.Max(1, definition.HarvestCapacity)
                : 0f;
            PaintUnit(definition, position, heading, motion.TurretHeadingOf(entity), owner, cargo);
            batch.End();
        }
    }

    /// <summary>A translucent footprint for the structure being placed: green where it can go, red where not.</summary>
    public void PaintPlacement(GameSimulation simulation, int structureType, Cell origin, bool valid)
    {
        var definition = simulation.Content.Structure(structureType);
        batch.Begin(float.MaxValue - 1, GroundHeight(simulation.Map, origin.X + (definition.Width / 2f), origin.Y + (definition.Height / 2f)));
        for (var y = 0; y < definition.Height; y++)
        {
            for (var x = 0; x < definition.Width; x++)
            {
                var cell = new Cell(origin.X + x, origin.Y + y);
                var ok = valid && simulation.Map.IsBuildable(cell);
                batch.Flat(cell.X + 0.04f, cell.Y + 0.04f, 0.92f, 0.92f, GroundEpsilon * 4, ok ? Palette.PlaceOk : Palette.PlaceBad, emissive: 1f);
            }
        }

        var tint = valid ? Palette.WithAlpha(Palette.Concrete, 0.35f) : Palette.WithAlpha(Palette.PlaceBad, 0.3f);
        batch.Box(origin.X + 0.1f, origin.Y + 0.1f, definition.Width - 0.2f, definition.Height - 0.2f, 0, StructureHeight(definition.Role), tint, tint, emissive: 0.5f);
        batch.End();
    }

    public static float StructureHeight(StructureRole role) => role switch
    {
        StructureRole.ConstructionYard => 1.0f,
        StructureRole.PowerPlant => 0.7f,
        StructureRole.Refinery => 0.75f,
        StructureRole.Barracks => 0.6f,
        StructureRole.Factory => 0.85f,
        _ => 0.7f,
    };

    private void PaintStructure(StructureDefinition definition, Cell origin, int owner, float progress, FrameScene scene)
    {
        var team = Palette.Player(owner);
        var x = origin.X + 0.08f;
        var y = origin.Y + 0.08f;
        var width = definition.Width - 0.16f;
        var depth = definition.Height - 0.16f;
        var height = StructureHeight(definition.Role);

        // Foundation slab.
        batch.Box(origin.X, origin.Y, definition.Width, definition.Height, 0, 0.06f, Palette.ConcreteDark, Palette.Scale(Palette.ConcreteDark, 1.1f));

        if (progress < 1f)
        {
            // Under construction: a translucent shell rising with progress.
            var shell = Palette.WithAlpha(Palette.Concrete, 0.55f);
            batch.Box(x, y, width, depth, 0.06f, 0.06f + (height * Math.Max(0.08f, progress)), shell, Palette.WithAlpha(team, 0.6f));
            return;
        }

        batch.Box(x, y, width, depth, 0.06f, height, Palette.Concrete, Palette.Scale(Palette.Concrete, 1.05f));

        // Team colour: a stripe round the visible top edge of the walls, and a roof marker.
        batch.Quad(
            new Vector3(x, y + depth + 0.005f, height - 0.12f),
            new Vector3(x + width, y + depth + 0.005f, height - 0.12f),
            new Vector3(x + width, y + depth + 0.005f, height - 0.04f),
            new Vector3(x, y + depth + 0.005f, height - 0.04f),
            Vector3.UnitY,
            team);
        batch.Quad(
            new Vector3(x + width + 0.005f, y, height - 0.12f),
            new Vector3(x + width + 0.005f, y + depth, height - 0.12f),
            new Vector3(x + width + 0.005f, y + depth, height - 0.04f),
            new Vector3(x + width + 0.005f, y, height - 0.04f),
            Vector3.UnitX,
            team);
        batch.Box(x + width - 0.55f, y + depth - 0.55f, 0.35f, 0.35f, height, height + 0.04f, Palette.Scale(team, 0.8f), team);

        var center = new Vector2(origin.X + (definition.Width / 2f), origin.Y + (definition.Height / 2f));
        switch (definition.Role)
        {
            case StructureRole.ConstructionYard:
                // Crane mast and boom.
                batch.Box(x + width - 0.45f, y + 0.2f, 0.18f, 0.18f, height, height + 0.9f, Palette.HarvesterYellow, Palette.HarvesterYellow);
                batch.Box(x + 0.2f, y + 0.2f, width - 0.5f, 0.12f, height + 0.85f, height + 0.95f, Palette.HarvesterYellow, Palette.HarvesterYellow);
                break;

            case StructureRole.PowerPlant:
                // Two cooling stacks with a glowing core between them.
                batch.Box(x + 0.15f, y + 0.15f, 0.4f, 0.4f, height, height + 0.55f, Palette.ConcreteDark, Palette.Metal);
                batch.Box(x + width - 0.55f, y + depth - 0.55f, 0.4f, 0.4f, height, height + 0.55f, Palette.ConcreteDark, Palette.Metal);
                batch.Disc(center, 0.22f, height + 0.08f, Palette.PowerGlow, emissive: 1f);
                scene.AddLight(new PointLight(new Vector3(center, height + 0.6f + batch.Lift), 3.2f, Palette.PowerGlow, 1.1f));
                break;

            case StructureRole.Refinery:
                // Silo holding glowing Cindrite, and a dock pad at the exit.
                batch.Box(x + 0.2f, y + 0.2f, 0.9f, 0.9f, height, height + 0.45f, Palette.Metal, Palette.Metal);
                batch.Disc(new Vector2(x + 0.65f, y + 0.65f), 0.32f, height + 0.46f, Palette.Cindrite, emissive: 0.9f);
                batch.Flat(origin.X + (definition.Width / 2f) - 0.5f, origin.Y + definition.Height + 0.02f, 1f, 0.6f, GroundEpsilon * 2, Palette.ConcreteDark);
                scene.AddLight(new PointLight(new Vector3(x + 0.65f, y + 0.65f, height + 0.8f + batch.Lift), 3f, Palette.Cindrite, 0.9f));
                break;

            case StructureRole.Barracks:
                batch.Box(x + 0.3f, y + depth - 0.1f, width - 0.6f, 0.1f, 0.06f, height * 0.7f, Palette.Metal, Palette.Metal);
                break;

            case StructureRole.Factory:
                // A big door on the front (+y) face and a vent stack.
                batch.Quad(
                    new Vector3(x + 0.5f, y + depth + 0.01f, 0.06f),
                    new Vector3(x + width - 0.5f, y + depth + 0.01f, 0.06f),
                    new Vector3(x + width - 0.5f, y + depth + 0.01f, height * 0.75f),
                    new Vector3(x + 0.5f, y + depth + 0.01f, height * 0.75f),
                    Vector3.UnitY,
                    Palette.Metal);
                batch.Box(x + 0.2f, y + 0.2f, 0.35f, 0.35f, height, height + 0.5f, Palette.Metal, Palette.ConcreteDark);
                break;
        }

        // A warm work light at the front corner of every finished structure.
        scene.AddLight(new PointLight(new Vector3(origin.X + definition.Width + 0.3f, origin.Y + definition.Height + 0.3f, 0.9f + batch.Lift), 2.6f, Palette.BaseLight, 0.55f));
    }

    private void PaintUnit(UnitDefinition definition, Vector2 position, float heading, float turret, int owner, float cargo)
    {
        var team = Palette.Player(owner);
        var dark = Palette.Scale(team, 0.65f);
        switch (definition.Id)
        {
            case BulwarkContent.Warden:
                batch.Disc(position + new Vector2(0.06f, -0.03f), 0.42f, GroundEpsilon, Palette.Shadow);
                batch.RotatedBox(position, 0.78f, 0.52f, heading, 0.04f, 0.24f, Palette.Metal, dark);
                batch.RotatedBox(position, 0.4f, 0.34f, turret, 0.24f, 0.38f, dark, team);
                var muzzle = position + (new Vector2(MathF.Cos(turret), MathF.Sin(turret)) * 0.36f);
                batch.RotatedBox((position + muzzle) / 2f + (new Vector2(MathF.Cos(turret), MathF.Sin(turret)) * 0.12f), 0.44f, 0.07f, turret, 0.29f, 0.34f, Palette.Metal, Palette.Metal);
                break;

            case BulwarkContent.Harvester:
                batch.Disc(position + new Vector2(0.06f, -0.03f), 0.52f, GroundEpsilon, Palette.Shadow);
                batch.RotatedBox(position, 0.95f, 0.62f, heading, 0.04f, 0.3f, Palette.Scale(Palette.HarvesterYellow, 0.8f), Palette.HarvesterYellow);
                var forward = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
                batch.RotatedBox(position + (forward * 0.3f), 0.28f, 0.5f, heading, 0.3f, 0.48f, dark, team);
                var bed = position - (forward * 0.15f);
                batch.RotatedBox(bed, 0.5f, 0.5f, heading, 0.3f, 0.34f + (cargo * 0.14f), Palette.Metal, cargo > 0.02f ? Palette.Cindrite : Palette.Metal, cargo > 0.02f ? 0.6f : 0f);
                break;

            default:
                // Infantry: a small squad figure. Rocketeers carry a launcher.
                batch.Disc(position + new Vector2(0.04f, -0.02f), 0.17f, GroundEpsilon, Palette.Shadow);
                batch.RotatedBox(position, 0.2f, 0.24f, heading, 0.02f, 0.32f, dark, team);
                batch.Disc(position, 0.08f, 0.4f, LinearColor.FromSrgb(210, 180, 150));
                if (definition.Id == BulwarkContent.Rocketeer)
                {
                    batch.RotatedBox(position + (new Vector2(MathF.Cos(turret + 1.2f), MathF.Sin(turret + 1.2f)) * 0.1f), 0.34f, 0.07f, turret, 0.3f, 0.37f, Palette.Metal, Palette.Metal);
                }

                break;
        }
    }

    private void PaintCrystals(int x, int y, int amount, float ground)
    {
        // Two or three crystal clusters per cell, brighter and bigger the richer the cell.
        var richness = Math.Clamp(amount / 500f, 0.15f, 1f);
        var seed = Hash(x, y);
        batch.Begin(x + y + 0.5f, ground);
        for (var i = 0; i < 3; i++)
        {
            var ox = 0.2f + ((seed >> (i * 4) & 0xF) / 15f * 0.6f);
            var oy = 0.2f + ((seed >> ((i * 4) + 12) & 0xF) / 15f * 0.6f);
            var size = (0.08f + ((seed >> (i * 3) & 0x7) / 7f * 0.08f)) * (0.6f + (richness * 0.6f));
            var color = Palette.Mix(Palette.Cindrite, Palette.CindriteHot, ((seed >> (i * 5)) & 0x3) / 3f * 0.5f);
            batch.Box(x + ox - size, y + oy - size, size * 2, size * 2, 0, size * 2.4f, Palette.Scale(color, 0.7f), color, emissive: 0.55f + (0.35f * richness));
        }

        batch.End();
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) VisibleCells(IsoCamera camera, GridMap map)
    {
        // The ground under the viewport's corners bounds the cells on screen (plus a margin for tall objects).
        var origin = camera.ViewportOrigin;
        var size = camera.ViewportSize;
        Span<Vector2> corners =
        [
            camera.ScreenToGround(origin),
            camera.ScreenToGround(origin + new Vector2(size.X, 0)),
            camera.ScreenToGround(origin + new Vector2(0, size.Y + 96)),
            camera.ScreenToGround(origin + size + new Vector2(0, 96)),
        ];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var corner in corners)
        {
            minX = Math.Min(minX, corner.X);
            minY = Math.Min(minY, corner.Y);
            maxX = Math.Max(maxX, corner.X);
            maxY = Math.Max(maxY, corner.Y);
        }

        return (
            Math.Clamp((int)MathF.Floor(minX) - 1, 0, map.Width - 1),
            Math.Clamp((int)MathF.Floor(minY) - 1, 0, map.Height - 1),
            Math.Clamp((int)MathF.Ceiling(maxX) + 1, 0, map.Width - 1),
            Math.Clamp((int)MathF.Ceiling(maxY) + 1, 0, map.Height - 1));
    }

    private static LinearColor Variation(LinearColor color, int x, int y, float amount) =>
        Palette.Scale(color, 1f + ((((Hash(x, y) & 0xFF) / 255f) - 0.5f) * 2f * amount));

    private static int Hash(int x, int y)
    {
        unchecked
        {
            var h = (x * 374761393) + (y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & 0x7FFFFFFF;
        }
    }
}
