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
                        // Earth broken by moss, in four sub-tiles so the patches don't follow the grid.
                        var lift = 1f + (map.HeightAt(cell) * 0.06f);
                        var stained = map.CindriteAt(cell) > 0;
                        for (var sub = 0; sub < 4; sub++)
                        {
                            var sx = x + ((sub & 1) * 0.5f);
                            var sy = y + ((sub >> 1) * 0.5f);
                            batch.Flat(sx, sy, 0.5f, 0.5f, top, Palette.Scale(GroundColor(sx + 0.25f, sy + 0.25f, stained), lift));
                        }

                        color = Palette.Scale(GroundColor(x + 0.5f, y + 0.5f, stained), lift);
                        break;
                }

                // Skirts down to lower neighbours on the two sides the camera sees, so steps have faces, not cracks:
                // bare earth darkening toward the rock beneath.
                var side = Palette.Mix(Palette.Scale(color, 0.55f), Palette.RockDark, 0.35f);
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
                        PaintRock(x, y, top);
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

                if (map.IsFissure(cell))
                {
                    // The vent itself stays open so it reads; its Cindrite shows as the crystals around it.
                    if (fog.At(x, y) > 0f)
                    {
                        PaintFissure(x, y, top, time);
                    }

                    continue;
                }

                var cindrite = map.CindriteAt(cell);
                if (cindrite > 0 && fog.At(x, y) > 0f)
                {
                    PaintCrystals(x, y, cindrite, top);
                }
                else if (map.TerrainAt(cell) == Terrain.Clear && map.StructureAt(cell) < 0)
                {
                    PaintGroundCover(x, y, top);
                }
            }
        }
    }

    // Earth with patches of moss (broad, soft noise) and darker soil (finer noise); Cindrite stains it green.
    private static LinearColor GroundColor(float x, float y, bool stained)
    {
        var moss = SmoothStep(0.42f, 0.68f, Noise(x, y, 5.5f, 11));
        var soil = Noise(x, y, 2.3f, 23);
        var earth = Palette.Mix(Palette.Ground, Palette.GroundDark, soil * 0.7f);
        var ground = Palette.Mix(earth, Palette.Mix(Palette.Moss, Palette.MossLight, Noise(x, y, 1.7f, 31)), moss * 0.85f);
        return stained ? Palette.Mix(ground, Palette.CindriteGround, 0.7f) : ground;
    }

    // Grass tufts on mossy ground and the odd stone on bare earth, so open ground has texture at a glance.
    private void PaintGroundCover(int x, int y, float ground)
    {
        var seed = Hash(x, y);
        var moss = Noise(x + 0.5f, y + 0.5f, 5.5f, 11);
        if (moss > 0.55f && seed % 3 == 0)
        {
            batch.Begin(x + y + 0.2f, ground);
            for (var i = 0; i < 3; i++)
            {
                var ox = 0.15f + (((seed >> (i * 5)) & 0xF) / 15f * 0.7f);
                var oy = 0.15f + (((seed >> ((i * 5) + 3)) & 0xF) / 15f * 0.7f);
                var tall = 0.06f + (((seed >> (i + 9)) & 0x3) * 0.025f);
                var blade = Palette.Mix(Palette.Grass, Palette.MossLight, ((seed >> (i * 2)) & 0x3) / 3f);
                batch.Box(x + ox - 0.04f, y + oy - 0.04f, 0.08f, 0.08f, 0, tall, Palette.Scale(blade, 0.7f), blade);
            }

            batch.End();
        }
        else if (moss < 0.35f && seed % 7 == 0)
        {
            batch.Begin(x + y + 0.2f, ground);
            var ox = 0.2f + ((seed >> 4 & 0xF) / 15f * 0.6f);
            var oy = 0.2f + ((seed >> 8 & 0xF) / 15f * 0.6f);
            var size = 0.07f + ((seed >> 12 & 0x3) * 0.03f);
            batch.Box(x + ox - size, y + oy - size, size * 2, size * 1.6f, 0, size * 0.9f, Palette.Scale(Palette.Pebble, 0.7f), Palette.Pebble);
            batch.End();
        }
    }

    // Cliffs and outcrops: dark slate from the lowest ground up past the cell's level, broken into a craggy top.
    private void PaintRock(int x, int y, float top)
    {
        var seed = Hash(x, y);
        batch.Begin(x + y + 1);
        var height = top + 0.28f + (seed % 4 * 0.05f);
        batch.Box(x, y, 1, 1, 0, height, Variation(Palette.Rock, x, y, 0.1f), Variation(Palette.RockTop, x, y, 0.1f));
        for (var i = 0; i < 2; i++)
        {
            var ox = 0.05f + (((seed >> (i * 6)) & 0xF) / 15f * 0.45f);
            var oy = 0.05f + (((seed >> ((i * 6) + 4)) & 0xF) / 15f * 0.45f);
            var size = 0.3f + (((seed >> (i + 14)) & 0x3) * 0.06f);
            var crag = height + 0.1f + (((seed >> (i * 3 + 18)) & 0x7) * 0.04f);
            batch.Box(x + ox, y + oy, size, size, height - 0.05f, crag, Palette.Scale(Palette.Rock, 0.85f), Variation(Palette.RockTop, x + i, y, 0.12f));
        }

        batch.End();
    }

    // Value noise: smooth, repeatable, 0..1, with features about `scale` cells across.
    private static float Noise(float x, float y, float scale, int salt)
    {
        var fx = x / scale;
        var fy = y / scale;
        var ix = (int)MathF.Floor(fx);
        var iy = (int)MathF.Floor(fy);
        var tx = SmoothStep(0f, 1f, fx - ix);
        var ty = SmoothStep(0f, 1f, fy - iy);
        float At(int cx, int cy) => (Hash(cx + (salt * 7919), cy - (salt * 104729)) & 0xFFFF) / 65535f;
        var top = At(ix, iy) + ((At(ix + 1, iy) - At(ix, iy)) * tx);
        var bottom = At(ix, iy + 1) + ((At(ix + 1, iy + 1) - At(ix, iy + 1)) * tx);
        return top + ((bottom - top) * ty);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private void PaintTree(int x, int y, float ground)
    {
        // One or two dark conifers per cell, three tiers each, jittered so woods look natural.
        var seed = Hash(x, y);
        batch.Begin(x + y + 1, ground);
        var count = 1 + (seed & 1);
        for (var i = 0; i < count; i++)
        {
            var ox = 0.25f + (((seed >> (4 + (i * 6))) & 0xF) / 15f * 0.5f);
            var oy = 0.25f + (((seed >> (8 + (i * 6))) & 0xF) / 15f * 0.5f);
            var size = 0.2f + (((seed >> (12 + i)) & 0x3) * 0.035f);
            var height = 0.62f + (((seed >> (16 + i)) & 0x3) * 0.1f);
            var shade = 0.85f + (((seed >> (20 + i)) & 0x3) * 0.08f);
            var leaves = Palette.Scale(Palette.Leaves, shade);
            var top = Palette.Scale(Palette.LeavesTop, shade);
            batch.Disc(new Vector2(x + ox + 0.1f, y + oy - 0.05f), size * 1.3f, GroundEpsilon, Palette.Shadow);
            batch.Box(x + ox - 0.035f, y + oy - 0.035f, 0.07f, 0.07f, 0, height * 0.3f, Palette.Trunk, Palette.Trunk);
            for (var tier = 0; tier < 3; tier++)
            {
                var spread = size * (1f - (tier * 0.28f));
                var low = height * (0.22f + (tier * 0.25f));
                batch.Box(x + ox - spread, y + oy - spread, spread * 2, spread * 2, low, low + (height * 0.3f), leaves, top);
            }
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
            PaintStructure(simulation, definition, info.Origin, owner, progress, scene);
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
            if ((owner != localPlayer && fog.Sample(position.X, position.Y) < 0.75f) || simulation.IsSheltered(entity))
            {
                continue; // enemies are only seen inside current vision; units in a bunker are inside it
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

    /// <summary>
    /// The Maw's creatures, inside current vision only: low skittering chaff, upright Spitters with a glowing acid sac,
    /// and hulking crystal-backed Siegebeasts. All in the Maw's violet with ember crystal, never a team colour.
    /// </summary>
    public void PaintCreatures(GameSimulation simulation, float alpha)
    {
        var world = simulation.World;
        var creatures = world.Store<Creature>();
        foreach (var index in creatures.EntityIndices)
        {
            var entity = world.EntityAt(index);
            var fallback = MotionTracker.ToVector(world.Store<Position>().Get(index).Value);
            var position = motion.PositionOf(entity, fallback, alpha);
            if (fog.Sample(position.X, position.Y) < 0.75f)
            {
                continue;
            }

            var heading = motion.HeadingOf(entity);
            var forward = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
            batch.Begin(position.X + position.Y, GroundHeight(simulation.Map, position.X, position.Y));
            var side = new Vector2(-forward.Y, forward.X);
            switch (simulation.Content.Creature(creatures.Get(index).CreatureType).Id)
            {
                case MawContent.Siegebeast:
                    // A hulking crimson carapace on six thick legs, a row of spines down its back, embers for eyes.
                    batch.Disc(position + new Vector2(0.08f, -0.04f), 0.6f, GroundEpsilon, Palette.Shadow);
                    Legs(position, forward, side, 3, 0.36f, 0.62f, 0.09f, 0.3f);
                    batch.RotatedBox(position, 0.95f, 0.72f, heading, 0.16f, 0.52f, Palette.MawCarapaceDark, Palette.MawCarapace);
                    batch.RotatedBox(position - (forward * 0.12f), 0.6f, 0.5f, heading, 0.52f, 0.64f, Palette.MawCarapace, Palette.MawFlesh);
                    batch.RotatedBox(position + (forward * 0.48f), 0.3f, 0.42f, heading, 0.14f, 0.42f, Palette.MawCarapaceDark, Palette.MawCarapace);
                    Eyes(position + (forward * 0.62f), side, 0.12f, 0.36f, 0.05f);
                    for (var spike = -1; spike <= 1; spike++)
                    {
                        var at = position - (forward * (0.1f + (spike * 0.22f)));
                        Spike(at, -forward * 0.12f, 0.14f, 0.62f, 0.95f - (Math.Abs(spike) * 0.1f), Palette.MawCarapaceDark, Palette.MawGlow, 0.5f);
                    }

                    break;

                case MawContent.Spitter:
                    // Upright on four legs with a swollen violet acid sac on its back.
                    batch.Disc(position + new Vector2(0.04f, -0.02f), 0.24f, GroundEpsilon, Palette.Shadow);
                    Legs(position, forward, side, 2, 0.18f, 0.32f, 0.04f, 0.16f);
                    batch.RotatedBox(position + (forward * 0.06f), 0.24f, 0.2f, heading, 0.1f, 0.34f, Palette.MawCarapaceDark, Palette.MawCarapace);
                    batch.Disc(position - (forward * 0.08f), 0.13f, 0.36f, Palette.MawViolet, emissive: 0.75f);
                    batch.Disc(position - (forward * 0.08f), 0.2f, 0.37f, Palette.WithAlpha(Palette.MawViolet, 0.35f), emissive: 1f, shape: QuadShape.Glow);
                    Eyes(position + (forward * 0.18f), side, 0.05f, 0.28f, 0.025f);
                    break;

                default:
                    // Skitter: a low crimson spider, legs splayed, ember eyes.
                    Legs(position, forward, side, 3, 0.13f, 0.24f, 0.025f, 0.08f);
                    batch.RotatedBox(position - (forward * 0.07f), 0.2f, 0.18f, heading, 0.05f, 0.16f, Palette.MawCarapaceDark, Palette.MawCarapace);
                    batch.RotatedBox(position + (forward * 0.07f), 0.12f, 0.12f, heading, 0.05f, 0.13f, Palette.MawCarapace, Palette.MawFlesh);
                    Eyes(position + (forward * 0.13f), side, 0.03f, 0.12f, 0.02f);
                    break;
            }

            batch.End();
        }
    }

    // Pairs of splayed legs either side of a creature, each a thin slab angling down from the body to the ground.
    private void Legs(Vector2 position, Vector2 forward, Vector2 side, int pairs, float hip, float reach, float thickness, float height)
    {
        for (var pair = 0; pair < pairs; pair++)
        {
            var along = pairs == 1 ? 0f : ((pair / (float)(pairs - 1)) - 0.5f) * hip * 1.6f;
            foreach (var sign in new[] { -1f, 1f })
            {
                var direction = Vector2.Normalize((side * sign) + (forward * (along / hip * 0.7f)));
                var knee = position + (forward * along) + (direction * reach * 0.6f);
                var angle = MathF.Atan2(direction.Y, direction.X);
                batch.RotatedBox(position + (forward * along) + (direction * reach * 0.3f), reach * 0.6f, thickness, angle, height * 0.7f, height, Palette.MawCarapaceDark, Palette.MawCarapace);
                batch.RotatedBox(knee + (direction * reach * 0.2f), reach * 0.45f, thickness, angle, 0f, height * 0.75f, Palette.MawCarapaceDark, Palette.MawCarapaceDark);
            }
        }
    }

    // Two glowing ember eyes on the front of a creature's head.
    private void Eyes(Vector2 head, Vector2 side, float spread, float z, float size)
    {
        batch.Disc(head + (side * spread), size, z, Palette.MawGlow, emissive: 1f);
        batch.Disc(head - (side * spread), size, z, Palette.MawGlow, emissive: 1f);
    }

    /// <summary>
    /// The ground where a telegraphed wave will emerge (shown through fog, GDD section 3): tremor rings spreading out
    /// from the nest's mouth over a hot glow, beating faster as the launch nears.
    /// </summary>
    public void PaintTelegraph(GameSimulation simulation, Cell source, float urgency, double time, LinearColor color)
    {
        var center = new Vector2(source.X + 0.5f, source.Y + 0.5f);
        batch.Begin(source.X + source.Y + 0.45f, GroundHeight(simulation.Map, center.X, center.Y));
        batch.Disc(center, 2.2f, GroundEpsilon * 4, Palette.WithAlpha(color, 0.25f + (0.25f * urgency)), emissive: 0.6f, shape: QuadShape.Glow);
        var speed = 0.8 + (2.2 * urgency);
        for (var ring = 0; ring < 3; ring++)
        {
            var phase = (float)(((time * speed) + (ring / 3.0)) % 1.0);
            batch.Disc(center, 0.6f + (phase * 3.4f), GroundEpsilon * 5, Palette.WithAlpha(color, 0.7f * (1f - phase)), emissive: 0.8f, shape: QuadShape.Ring);
        }

        batch.End();
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
        StructureRole.Defence => 0.5f,
        StructureRole.ConstructionYard => 1.15f,
        StructureRole.PowerPlant => 0.8f,
        StructureRole.Refinery => 0.85f,
        StructureRole.Barracks => 0.75f,
        StructureRole.Factory => 0.95f,
        _ => 0.7f,
    };

    // A paved plate under every building, slightly larger than its footprint, with seams on the cell lines and
    // hazard striping before the doors of anything that rolls units out.
    private void PaintPlate(StructureDefinition definition, Cell origin)
    {
        var margin = definition.Width > 1 ? 0.22f : 0.08f;
        batch.Flat(origin.X - margin, origin.Y - margin, definition.Width + (2 * margin), definition.Height + (2 * margin), GroundEpsilon * 2, Palette.Pavement);
        for (var i = 1; i < definition.Width; i++)
        {
            batch.Flat(origin.X + i - 0.015f, origin.Y - margin, 0.03f, definition.Height + (2 * margin), GroundEpsilon * 3, Palette.PavementSeam);
        }

        for (var i = 1; i < definition.Height; i++)
        {
            batch.Flat(origin.X - margin, origin.Y + i - 0.015f, definition.Width + (2 * margin), 0.03f, GroundEpsilon * 3, Palette.PavementSeam);
        }

        if (definition.Role is StructureRole.Barracks or StructureRole.Factory or StructureRole.RepairBay or StructureRole.Refinery)
        {
            for (var stripe = 0; stripe < definition.Width * 3; stripe++)
            {
                if (stripe % 2 == 0)
                {
                    batch.Flat(origin.X + (stripe / 3f), origin.Y + definition.Height + margin - 0.1f, 1f / 3f, 0.1f, GroundEpsilon * 4, Palette.Hazard);
                }
            }
        }

        batch.Box(origin.X, origin.Y, definition.Width, definition.Height, 0, 0.06f, Palette.ConcreteDark, Palette.Scale(Palette.ConcreteDark, 1.1f));
    }

    // A flat panel on a building's front (+y) face, from x0 to x1 and z0 to z1.
    private void FacePanelY(float x0, float x1, float face, float z0, float z1, LinearColor color, float emissive = 0f) =>
        batch.Quad(new Vector3(x0, face, z0), new Vector3(x1, face, z0), new Vector3(x1, face, z1), new Vector3(x0, face, z1), Vector3.UnitY, color, emissive);

    // A flat panel on a building's side (+x) face.
    private void FacePanelX(float face, float y0, float y1, float z0, float z1, LinearColor color, float emissive = 0f) =>
        batch.Quad(new Vector3(face, y0, z0), new Vector3(face, y1, z0), new Vector3(face, y1, z1), new Vector3(face, y0, z1), Vector3.UnitX, color, emissive);

    // Which way a guard tower's guns point: at its target, or south-east at rest.
    private static float TowerAim(GameSimulation simulation, Cell origin)
    {
        var world = simulation.World;
        var tower = world.EntityAt(simulation.Map.StructureAt(origin));
        if (tower.IsNone || !world.Has<Armament>(tower) || world.Get<Armament>(tower).Target is var target && (target.IsNone || !world.IsAlive(target)))
        {
            return MathF.PI / 4f;
        }

        var from = MotionTracker.ToVector(world.Get<Position>(tower).Value);
        var to = MotionTracker.ToVector(world.Get<Position>(world.Get<Armament>(tower).Target).Value);
        return MathF.Atan2(to.Y - from.Y, to.X - from.X);
    }

    private static int OccupantsAt(GameSimulation simulation, Cell origin)
    {
        var bunker = simulation.World.EntityAt(simulation.Map.StructureAt(origin));
        return bunker.IsNone ? 0 : simulation.OccupantsOf(bunker);
    }

    private void PaintStructure(GameSimulation simulation, StructureDefinition definition, Cell origin, int owner, float progress, FrameScene scene)
    {
        if (definition.Role == StructureRole.Nest)
        {
            PaintNest(definition, origin, scene);
            return;
        }

        if (definition.Role == StructureRole.Wall)
        {
            // A plain block, shorter while it goes up. No light: a wall line is dozens of segments.
            var top = 0.45f * Math.Max(0.15f, progress);
            batch.Box(origin.X + 0.04f, origin.Y + 0.04f, 0.92f, 0.92f, 0, top, Palette.Scale(Palette.Wall, 0.8f), Palette.Wall);
            batch.Box(origin.X + 0.04f, origin.Y + 0.86f, 0.92f, 0.1f, top - 0.08f, top, Palette.Player(owner), Palette.Player(owner));
            return;
        }

        var team = Palette.Player(owner);
        var x = origin.X + 0.08f;
        var y = origin.Y + 0.08f;
        var width = definition.Width - 0.16f;
        var depth = definition.Height - 0.16f;
        var height = StructureHeight(definition.Role);

        PaintPlate(definition, origin);

        if (progress < 1f)
        {
            // Under construction: a translucent shell rising with progress inside a scaffold of corner posts.
            var shell = Palette.WithAlpha(Palette.Concrete, 0.55f);
            var reached = 0.06f + (height * Math.Max(0.08f, progress));
            batch.Box(x, y, width, depth, 0.06f, reached, shell, Palette.WithAlpha(team, 0.6f));
            foreach (var (px, py) in new[] { (x, y + depth - 0.06f), (x + width - 0.06f, y + depth - 0.06f), (x + width - 0.06f, y) })
            {
                batch.Box(px, py, 0.06f, 0.06f, 0.06f, height + 0.1f, Palette.HarvesterYellow, Palette.HarvesterYellow);
            }

            return;
        }

        // A gunmetal hull in two tiers: a broad base and a narrower upper deck set back from the front.
        var deck = height * 0.62f;
        batch.Box(x, y, width, depth, 0.06f, deck, Palette.Concrete, Palette.Scale(Palette.Concrete, 1.08f));
        var upperX = x + 0.12f;
        var upperY = y + 0.08f;
        var upperWidth = width - 0.42f;
        var upperDepth = depth - 0.5f;
        batch.Box(upperX, upperY, upperWidth, upperDepth, deck, height, Palette.ConcreteDark, Palette.Concrete);

        // Faction colour, big and readable: panels down the front, a trim band round the base tier, a banner.
        var front = y + depth + 0.006f;
        var side = x + width + 0.006f;
        var panels = Math.Max(1, definition.Width - 1);
        for (var i = 0; i < panels; i++)
        {
            var left = x + 0.3f + (i * (width - 0.6f) / panels);
            FacePanelY(left, left + 0.24f, front, 0.14f, deck - 0.14f, Palette.Scale(team, 0.8f));
        }

        FacePanelX(side, y + 0.25f, y + 0.5f, 0.14f, deck - 0.14f, Palette.Scale(team, 0.7f));
        FacePanelY(x, x + width, front, deck - 0.09f, deck - 0.03f, team);
        FacePanelX(side, y, y + depth, deck - 0.09f, deck - 0.03f, team);

        // Lit windows along the upper deck.
        FacePanelY(upperX + 0.12f, upperX + upperWidth - 0.12f, upperY + upperDepth + 0.006f, height - 0.16f, height - 0.1f, Palette.BaseLight, 0.9f);

        // The banner: hung from the front of the upper deck, the faction's colour with a pale chevron.
        var bannerX = upperX + upperWidth - 0.34f;
        var bannerFace = upperY + upperDepth + 0.012f;
        FacePanelY(bannerX, bannerX + 0.24f, bannerFace, deck - 0.05f, height + 0.08f, team, 0.15f);
        FacePanelY(bannerX + 0.08f, bannerX + 0.16f, bannerFace + 0.002f, height - 0.18f, height - 0.04f, Palette.Text, 0.3f);
        batch.Box(bannerX - 0.03f, bannerFace - 0.03f, 0.3f, 0.03f, height + 0.08f, height + 0.11f, Palette.Metal, Palette.Metal);

        // Roof clutter: vents and a faction marker.
        batch.Box(upperX + 0.12f, upperY + 0.12f, 0.2f, 0.2f, height, height + 0.08f, Palette.Metal, Palette.ConcreteDark);
        batch.Box(upperX + upperWidth - 0.4f, upperY + 0.12f, 0.26f, 0.26f, height, height + 0.03f, Palette.Scale(team, 0.7f), team);

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

            case StructureRole.Defence when definition.Weapon is not null:
                // A guard tower: a post with a gun housing that turns toward whatever it shoots at.
                batch.Box(x + 0.15f, y + 0.15f, width - 0.3f, depth - 0.3f, height, height + 0.35f, Palette.ConcreteDark, Palette.Concrete);
                var aim = TowerAim(simulation, origin);
                var gun = new Vector2(center.X, center.Y);
                batch.RotatedBox(gun, 0.5f, 0.4f, aim, height + 0.35f, height + 0.6f, Palette.Scale(team, 0.7f), team);
                batch.RotatedBox(gun + (new Vector2(MathF.Cos(aim), MathF.Sin(aim)) * 0.35f), 0.4f, 0.08f, aim, height + 0.45f, height + 0.52f, Palette.Metal, Palette.Metal);
                break;

            case StructureRole.Defence:
                // A bunker: a squat block with a dark firing slit and a pip on the roof per occupant.
                batch.Quad(
                    new Vector3(x + 0.2f, y + depth + 0.01f, height - 0.3f),
                    new Vector3(x + width - 0.2f, y + depth + 0.01f, height - 0.3f),
                    new Vector3(x + width - 0.2f, y + depth + 0.01f, height - 0.18f),
                    new Vector3(x + 0.2f, y + depth + 0.01f, height - 0.18f),
                    Vector3.UnitY,
                    Palette.FissureDepth);
                var occupants = OccupantsAt(simulation, origin);
                for (var pip = 0; pip < definition.Garrison; pip++)
                {
                    var color = pip < occupants ? Palette.TextGood : Palette.ConcreteDark;
                    batch.Box(x + 0.25f + (pip * 0.32f), y + 0.25f, 0.2f, 0.2f, height, height + 0.06f, color, color, emissive: pip < occupants ? 0.6f : 0f);
                }

                break;

            case StructureRole.RepairBay:
                // An open service pad in front, under a yellow gantry with a green work lamp.
                batch.Flat(origin.X + 0.5f, origin.Y + definition.Height + 0.02f, definition.Width - 1f, 0.9f, GroundEpsilon * 2, Palette.ConcreteDark);
                batch.Box(x + 0.25f, y + depth - 0.2f, 0.14f, 0.14f, height, height + 0.6f, Palette.HarvesterYellow, Palette.HarvesterYellow);
                batch.Box(x + width - 0.39f, y + depth - 0.2f, 0.14f, 0.14f, height, height + 0.6f, Palette.HarvesterYellow, Palette.HarvesterYellow);
                batch.Box(x + 0.25f, y + depth - 0.2f, width - 0.5f, 0.14f, height + 0.55f, height + 0.65f, Palette.HarvesterYellow, Palette.HarvesterYellow);
                batch.Disc(new Vector2(center.X, y + depth - 0.13f), 0.1f, height + 0.66f, Palette.TextGood, emissive: 1f);
                scene.AddLight(new PointLight(new Vector3(center.X, origin.Y + definition.Height + 0.4f, 1.2f + batch.Lift), 2.4f, Palette.TextGood, 0.7f));
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

    // A Maw nest: a spire of black-and-crimson spikes round a glowing core, on a stain of violet creep. Higher tiers
    // grow taller, ring themselves with more spikes and sprout violet crystal.
    private void PaintNest(StructureDefinition definition, Cell origin, FrameScene scene)
    {
        var tier = definition.Id switch
        {
            MawContent.BehemothDen => 3,
            MawContent.BroodNest => 2,
            _ => 1,
        };
        var center = new Vector2(origin.X + (definition.Width / 2f), origin.Y + (definition.Height / 2f));
        var radius = definition.Width / 2f;
        var seed = Hash(origin.X, origin.Y);

        // The creep: a dark violet stain wider than the nest, veined with brighter channels running out from it.
        batch.Disc(center, radius + 1.6f, GroundEpsilon * 2, Palette.WithAlpha(Palette.Creep, 0.75f), shape: QuadShape.Glow);
        batch.Disc(center, radius + 0.7f, GroundEpsilon * 3, Palette.Creep);
        for (var vein = 0; vein < 7; vein++)
        {
            var heading = (vein * MathF.Tau / 7f) + (((seed >> (vein * 3)) & 0x7) * 0.09f);
            var middle = center + (Direction(heading) * (radius + 0.4f));
            var end = middle + (Direction(heading + ((((seed >> (vein * 2)) & 0x3) - 1.5f) * 0.3f)) * (0.6f + (((seed >> vein) & 0x3) * 0.25f)));
            Crack(center, middle, 0.05f, GroundEpsilon * 4, Palette.CreepVein, 0.35f);
            Crack(middle, end, 0.035f, GroundEpsilon * 4, Palette.CreepVein, 0.25f);
        }

        // The mound: dark carapace stepping in towards the top.
        var height = 0.9f + (0.5f * tier);
        var steps = 4;
        for (var step = 0; step < steps; step++)
        {
            var inset = 0.45f + (step * (radius - 0.75f) / steps);
            var bottom = step * height * 0.1f;
            var top = (step + 1) * height * 0.1f;
            var shade = Palette.Mix(Palette.MawCarapaceDark, Palette.MawCarapace, step / (float)steps * 0.6f);
            batch.Box(origin.X + inset, origin.Y + inset, definition.Width - (2 * inset), definition.Height - (2 * inset), bottom, top, shade, Palette.Scale(shade, 1.15f));
        }

        // The core: a hot red eye at the top of the mound, and the central spire rising out of it.
        var lip = steps * height * 0.1f;
        batch.Disc(center, 0.42f, lip + 0.01f, Palette.MawGlow, emissive: 1f);
        batch.Disc(center, 0.9f, lip + 0.02f, Palette.WithAlpha(Palette.MawGlow, 0.35f), emissive: 1f, shape: QuadShape.Glow);
        Spike(center + new Vector2(0.18f, -0.18f), new Vector2(-0.1f, 0.1f), 0.3f, lip * 0.5f, height + 1.1f, Palette.MawCarapaceDark, Palette.MawCarapace, 0f);

        // A ring of curved spikes leaning out, crimson and black by turns.
        var count = 5 + (tier * 3);
        for (var i = 0; i < count; i++)
        {
            var angle = (i * MathF.Tau / count) + ((seed >> (i % 8)) & 0x3) * 0.12f;
            var out_ = Direction(angle);
            var reach = radius - 0.5f - ((i % 2) * 0.45f);
            var tall = (height * (0.85f + ((i % 3) * 0.22f))) + (0.15f * tier);
            var crimson = i % 2 == 0;
            Spike(center + (out_ * reach), out_ * 0.55f, 0.24f + (0.03f * tier), 0, tall,
                crimson ? Palette.MawCarapace : Palette.MawCarapaceDark, crimson ? Palette.MawFlesh : Palette.MawCarapace, 0f);
        }

        // Older nests grow violet crystal from the creep.
        for (var i = 0; i < (tier - 1) * 4; i++)
        {
            var angle = 0.4f + (i * MathF.Tau / ((tier - 1) * 4));
            var at = center + (Direction(angle) * (radius + 0.25f + ((i % 2) * 0.4f)));
            Spike(at, Direction(angle) * 0.12f, 0.1f, 0, 0.5f + ((i % 3) * 0.18f), Palette.Scale(Palette.MawViolet, 0.7f), Palette.MawViolet, 0.7f);
        }

        scene.AddLight(new PointLight(new Vector3(center, lip + 0.7f + batch.Lift), 2.8f + tier, Palette.MawGlow, 1.1f));
        if (tier > 1)
        {
            scene.AddLight(new PointLight(new Vector3(center + new Vector2(radius + 0.4f, radius + 0.4f), 0.6f + batch.Lift), 2.2f, Palette.MawViolet, 0.5f));
        }
    }

    // A tapering spike of stacked, shrinking blocks from foot up to top, its tip pushed out by lean.
    private void Spike(Vector2 foot, Vector2 lean, float width, float bottom, float top, LinearColor sides, LinearColor tip, float emissive)
    {
        const int Segments = 4;
        for (var segment = 0; segment < Segments; segment++)
        {
            var t = segment / (float)Segments;
            var half = width * (1f - (t * 0.8f)) / 2f;
            var at = foot + (lean * t * t);
            var z0 = bottom + ((top - bottom) * t);
            var z1 = bottom + ((top - bottom) * (segment + 1) / Segments);
            var color = Palette.Mix(sides, tip, t);
            batch.Box(at.X - half, at.Y - half, half * 2, half * 2, z0, z1, color, Palette.Mix(color, tip, 0.5f), emissive * (0.4f + t));
        }
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
                else if (definition.Id == BulwarkContent.Medic)
                {
                    // A white pack with a red cross.
                    var back = position - (new Vector2(MathF.Cos(heading), MathF.Sin(heading)) * 0.1f);
                    batch.RotatedBox(back, 0.14f, 0.16f, heading, 0.14f, 0.34f, Palette.Text, Palette.Text);
                    batch.RotatedBox(back, 0.1f, 0.03f, heading, 0.341f, 0.345f, Palette.HealthLow, Palette.HealthLow);
                    batch.RotatedBox(back, 0.03f, 0.1f, heading, 0.341f, 0.345f, Palette.HealthLow, Palette.HealthLow);
                }
                else if (definition.Id == BulwarkContent.Pyro)
                {
                    // Fuel tanks on the back and a pilot light at the nozzle.
                    var back = position - (new Vector2(MathF.Cos(heading), MathF.Sin(heading)) * 0.1f);
                    batch.RotatedBox(back, 0.1f, 0.18f, heading, 0.14f, 0.36f, Palette.HarvesterYellow, Palette.HarvesterYellow);
                    batch.Disc(position + (new Vector2(MathF.Cos(turret), MathF.Sin(turret)) * 0.16f), 0.03f, 0.3f, Palette.Flame, emissive: 1f);
                }

                break;
        }
    }

    private void PaintFissure(int x, int y, float ground, double time)
    {
        // A scorched patch split by a jagged crack whose ember core breathes: the vent the field regrows from.
        var seed = Hash(x, y);
        var center = new Vector2(x + 0.5f, y + 0.5f);
        batch.Begin(x + y + 0.4f, ground);
        batch.Disc(center, 0.48f, GroundEpsilon, Palette.Fissure);
        var glow = 0.5f + (0.5f * MathF.Sin((float)(time * 1.7) + (seed % 628 / 100f)));
        var core = Palette.Mix(Palette.Cindrite, Palette.CindriteHot, glow * 0.6f);
        var angle = (seed & 0xFF) / 255f * MathF.Tau;
        for (var arm = 0; arm < 3; arm++)
        {
            // Each arm bends once on its way out from the middle.
            var heading = angle + (arm * MathF.Tau / 3f) + ((((seed >> (8 + (arm * 4))) & 0xF) - 7) * 0.06f);
            var bend = heading + ((((seed >> (20 + (arm * 3))) & 0x7) - 3) * 0.18f);
            var middle = center + (Direction(heading) * 0.18f);
            var end = middle + (Direction(bend) * (0.12f + (((seed >> (arm * 5)) & 0x3) * 0.03f)));
            Crack(center, middle, 0.07f, GroundEpsilon * 2, Palette.FissureDepth, 0f);
            Crack(middle, end, 0.05f, GroundEpsilon * 2, Palette.FissureDepth, 0f);
            Crack(center, middle, 0.03f, GroundEpsilon * 3, core, 0.7f + (0.3f * glow));
            Crack(middle, end, 0.02f, GroundEpsilon * 3, core, 0.5f + (0.3f * glow));
        }

        batch.Disc(center, 0.09f, GroundEpsilon * 3, core, emissive: 0.8f + (0.2f * glow));
        batch.End();
    }

    // A thin strip on the ground from a to b.
    private void Crack(Vector2 a, Vector2 b, float halfWidth, float z, LinearColor color, float emissive)
    {
        var along = b - a;
        if (along.LengthSquared() < 1e-6f)
        {
            return;
        }

        var side = Vector2.Normalize(new Vector2(-along.Y, along.X)) * halfWidth;
        batch.Quad(
            new Vector3(a - side, z), new Vector3(b - side, z), new Vector3(b + side, z), new Vector3(a + side, z),
            Vector3.UnitZ, color, emissive);
    }

    private static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    private void PaintCrystals(int x, int y, int amount, float ground)
    {
        // Clusters of acid-green shards leaning out of a glowing stain, taller and brighter the richer the cell.
        var richness = Math.Clamp(amount / 500f, 0.15f, 1f);
        var seed = Hash(x, y);
        batch.Begin(x + y + 0.5f, ground);
        batch.Disc(new Vector2(x + 0.5f, y + 0.5f), 0.55f + (richness * 0.25f), GroundEpsilon * 2, Palette.WithAlpha(Palette.Cindrite, 0.16f + (richness * 0.14f)),
            emissive: 0.8f, shape: QuadShape.Glow);
        var root = new Vector2(x + 0.35f + (((seed >> 20) & 0x7) / 7f * 0.3f), y + 0.35f + (((seed >> 23) & 0x7) / 7f * 0.3f));
        batch.Disc(root, 0.2f + (richness * 0.12f), GroundEpsilon * 3, Palette.CindriteDeep);
        var shards = 3 + ((seed >> 3) & 0x1) + (richness > 0.6f ? 1 : 0);
        for (var i = 0; i < shards; i++)
        {
            // The first shard stands tall in the middle; the rest lean out round it.
            var angle = ((seed >> (i * 3)) & 0x7) / 8f * MathF.Tau + (i * 2.1f);
            var lean = i == 0 ? Vector2.Zero : Direction(angle) * (0.14f + (((seed >> (i * 4)) & 0x3) * 0.04f));
            var foot = root + (i == 0 ? Vector2.Zero : Direction(angle) * 0.1f);
            var width = (0.11f + (((seed >> (i * 3)) & 0x7) / 7f * 0.06f)) * (0.75f + (richness * 0.4f)) * (i == 0 ? 1.25f : 1f);
            var height = (i == 0 ? 0.55f : 0.26f + (((seed >> ((i * 2) + 7)) & 0x7) / 7f * 0.22f)) * (0.55f + (richness * 0.65f));
            var body = Palette.Mix(Palette.CindriteDeep, Palette.Cindrite, 0.55f + (((seed >> (i * 5)) & 0x3) / 3f * 0.45f));
            Spike(foot, lean, width, 0, height, body, Palette.CindriteHot, 0.2f + (0.25f * richness));
        }

        batch.End();
    }

    /// <summary>Green light thrown by the richest Cindrite on screen (one light per few cells, so fields glow without using up every light).</summary>
    public void LightCindrite(GameSimulation simulation, IsoCamera camera, FrameScene scene)
    {
        var map = simulation.Map;
        var (minX, minY, maxX, maxY) = VisibleCells(camera, map);
        for (var y = minY - (minY % 3); y <= maxY; y += 3)
        {
            for (var x = minX - (minX % 3); x <= maxX; x += 3)
            {
                var cell = new Cell(x + 1, y + 1);
                if (map.InBounds(cell) && map.CindriteAt(cell) > 150 && fog.At(cell.X, cell.Y) > 0f)
                {
                    var ground = GroundHeight(map, cell.X + 0.5f, cell.Y + 0.5f);
                    scene.AddLight(new PointLight(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, ground + 0.6f), 2.6f, Palette.Cindrite, 0.45f));
                }
            }
        }
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
