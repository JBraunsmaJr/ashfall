using Ashfall.Core;
using Ashfall.Simulation.Map;

namespace Ashfall.MapGen;

/// <summary>
/// Builds a map from an archetype, seed and sliders (GDD section 6). Layout first, terrain second:
/// <list type="number">
/// <item>Layout graph: start, expansion, flank and centre nodes and the lanes between them, placed in one
/// sector of the map and copied to every rotation, so all starts are identical by construction.</item>
/// <item>Heightfield: symmetrised integer fBm quantised into the levels the Elevation slider allows,
/// flattened under bases and pads.</item>
/// <item>Water: rivers with island nodes (Riverlands) or lakes in the lowest ground (Crossroads).</item>
/// <item>Cliffs on every level change, turned into ramps where a lane crosses; bridges where lanes cross water.</item>
/// <item>Dressing: rock outcrops (Chokepoints) and woodland (Vegetation), never on lanes or pads.</item>
/// <item>Resources: Cindrite fields at the base, expansion, flank and centre nodes.</item>
/// <item>Validation and fairness scoring; a failing layout is regenerated from a new sub-seed, and after
/// <see cref="MaxAttempts"/> failures the plain fallback (no cliffs, water or clutter) is used.</item>
/// </list>
/// Integer maths only, no unordered collections: every peer builds the identical grid.
/// </summary>
public static class MapGenerator
{
    /// <summary>Bumped whenever the same settings would produce a different map.</summary>
    public const int Version = 1;

    public const int MaxAttempts = 8;

    public const int MinPlayers = 2;

    public const int MaxPlayers = 4;

    /// <summary>Generates the map for <paramref name="players"/> players (2–4).</summary>
    public static GeneratedMap Generate(MapSettings settings, int players)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(players, MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, MaxPlayers);
        var effective = ApplyLimits(settings, out var clamps);
        var rejected = new List<string>();
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var built = new Builder(effective, players, attempt, plain: false).Build();
            var report = MapValidator.Validate(built.Map, built.Starts, players, built.Order);
            if (report.Valid)
            {
                return Finish(built, report, settings, effective, clamps, attempt + 1, fellBack: false, rejected);
            }

            rejected.Add(report.Failures[0]);
        }

        var fallback = new Builder(effective, players, 0, plain: true).Build();
        return Finish(fallback, MapValidator.Validate(fallback.Map, fallback.Starts, players, fallback.Order), settings, effective, clamps, MaxAttempts, fellBack: true, rejected);
    }

    /// <summary>Map edge length for these settings.</summary>
    public static int SizeOf(MapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var (min, max) = settings.Archetype.SizeRange;
        var size = min + ((max - min) * settings[MapSlider.Size] / 100);
        return size / 8 * 8;
    }

    /// <summary>
    /// Applies the archetype's limits: each slider's allowed range, then the cap on how much of the map
    /// water, rock and trees may cover together (Vegetation gives way first, then Chokepoints).
    /// </summary>
    public static MapSettings ApplyLimits(MapSettings settings, out IReadOnlyList<string> clamps)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var archetype = settings.Archetype;
        var notes = new List<string>();
        var result = settings;
        foreach (var slider in Enum.GetValues<MapSlider>())
        {
            var rule = archetype.Rule(slider);
            var value = result[slider];
            var clamped = rule.Clamp(value);
            if (clamped != value)
            {
                notes.Add($"{Name(slider)} limited to {clamped} on {archetype.Name}");
                result = result.With(slider, clamped);
            }
        }

        var water = archetype.Lakes ? LakeShare(result) : 0;
        var budget = archetype.MaxObstruction - water;
        foreach (var slider in new[] { MapSlider.Vegetation, MapSlider.Chokepoints })
        {
            var other = slider == MapSlider.Vegetation ? OutcropShare(result) : TreeShare(result);
            var share = slider == MapSlider.Vegetation ? TreeShare(result) : OutcropShare(result);
            if (water + other + share <= archetype.MaxObstruction)
            {
                continue;
            }

            var allowed = budget - other;
            var value = allowed <= 0 ? 0 : allowed * 100 / MaxClutterShare;
            if (value < result[slider])
            {
                notes.Add($"{Name(slider)} limited to {value}: too little open ground with this much water and rock");
                result = result.With(slider, value);
            }
        }

        clamps = notes;
        return result;
    }

    private const int MaxClutterShare = 25;

    // Percent of the map each feature covers at its slider value.
    private static int LakeShare(MapSettings settings) => settings[MapSlider.Water] * 20 / 100;

    private static int OutcropShare(MapSettings settings) => settings[MapSlider.Chokepoints] * MaxClutterShare / 100;

    private static int TreeShare(MapSettings settings) => settings[MapSlider.Vegetation] * MaxClutterShare / 100;

    private static string Name(MapSlider slider) => slider switch
    {
        MapSlider.BaseSpacing => "Base spacing",
        _ => slider.ToString(),
    };

    private static GeneratedMap Finish(Built built, MapReport report, MapSettings requested, MapSettings effective, IReadOnlyList<string> clamps, int attempts, bool fellBack, IReadOnlyList<string> rejected) => new()
    {
        Rejected = rejected,
        Map = built.Map,
        StartOrigins = built.Starts,
        Requested = requested,
        Effective = effective,
        Clamps = clamps,
        Report = report,
        Attempts = attempts,
        FellBack = fellBack,
        SymmetryOrder = built.Order,
    };

    private sealed record Built(GridMap Map, Cell[] Starts, int Order);

    /// <summary>One generation attempt. Per-cell working state lives in flat arrays indexed y * size + x.</summary>
    private sealed class Builder
    {
        private const int YardSize = 3;

        private readonly MapSettings _settings;
        private readonly MapArchetype _archetype;
        private readonly int _players;
        private readonly bool _plain;
        private readonly int _size;
        private readonly MapSymmetry _symmetry;
        private readonly MapSymmetry _quarter;
        private readonly DeterministicRandom _random;
        private readonly ulong _seed;
        private readonly int[] _level;
        private readonly Terrain[] _terrain;
        private readonly bool[] _lane;
        private readonly bool[] _pad;
        private readonly int[] _cindrite;
        private readonly bool[] _fissure;

        public Builder(MapSettings settings, int players, int attempt, bool plain)
        {
            _settings = settings;
            _archetype = settings.Archetype;
            _players = players;
            _plain = plain;
            _size = SizeOf(settings);
            _symmetry = new MapSymmetry(_size, MapSymmetry.OrderFor(players));
            _quarter = new MapSymmetry(_size, 4);
            _seed = ValueNoise.Hash(settings.Seed, attempt, Version, 0xA5F);
            _random = new DeterministicRandom(_seed, 17);
            var cells = _size * _size;
            _level = new int[cells];
            _terrain = new Terrain[cells];
            _lane = new bool[cells];
            _pad = new bool[cells];
            _cindrite = new int[cells];
            _fissure = new bool[cells];
        }

        private int Order => _symmetry.Order;

        private int Last => _size - 1;

        public Built Build()
        {
            var layout = PlanLayout();
            PaintLanes(layout);
            PaintHeights(layout);
            if (!_plain)
            {
                PaintWater(layout);
            }

            PaintCliffs();
            if (!_plain)
            {
                PaintClutter(MapSlider.Chokepoints, Terrain.Rock, 0xB0C, OutcropShare(_settings));
                PaintClutter(MapSlider.Vegetation, Terrain.Trees, 0x7EE, TreeShare(_settings));
            }

            PaintFields(layout);
            return new Built(ToGrid(), StartOrigins(layout), Order);
        }

        // ---- layout ---------------------------------------------------------------------------------

        private sealed record Layout(Cell Start, Cell Expansion, Cell Flank, Cell Centre, Cell BaseField, int LaneWidth, int PlateauRadius, bool EdgeStart);

        private Layout PlanLayout()
        {
            var spacing = _settings[MapSlider.BaseSpacing];
            var closest = _size * 26 / 100;
            var inset = 7 + ((closest - 7) * (100 - spacing) / 100);
            // Edge starts only for duels: four bases on the edge midpoints would sit too close together.
            var edge = _archetype.EdgeStarts && Order == 2 && _random.Next(2) == 1;
            var middle = (_size / 2) - 1;
            var start = edge ? new Cell(inset + 1, middle) : new Cell(inset + 1, inset + 1);
            var centre = new Cell(middle, middle);

            // Towards the centre, pushed sideways a little so lanes don't all run straight down the diagonals.
            var reach = _archetype.Rivers ? 40 : 45 + _random.Next(11);
            var sideways = _archetype.Rivers ? 0 : _random.Next(-(_size / 20), (_size / 20) + 1);
            var toward = new Cell(Sign(centre.X - start.X), Sign(centre.Y - start.Y));
            var side = new Cell(toward.Y, -toward.X);
            var expansion = new Cell(
                start.X + ((centre.X - start.X) * reach / 100) + (side.X * sideways),
                start.Y + ((centre.Y - start.Y) * reach / 100) + (side.Y * sideways));

            var turned = _quarter.Rotate(start, 1);
            var flank = Order == 2 ? turned : new Cell((start.X + turned.X) / 2, (start.Y + turned.Y) / 2);

            // The base field sits beside the base, on the side away from the centre where possible.
            var baseField = new Cell(start.X + (side.X * 5) + toward.X, start.Y + (side.Y * 5) + toward.Y);
            var laneWidth = 7 - (_settings[MapSlider.Chokepoints] * 4 / 100);
            var plateau = _archetype.Rivers ? 10 : 11;
            return new Layout(start, expansion, flank, centre, baseField, laneWidth, plateau, edge);
        }

        private Cell[] StartOrigins(Layout layout)
        {
            var starts = new Cell[_players];
            var origin = new Cell(layout.Start.X - 1, layout.Start.Y - 1);
            for (var player = 0; player < _players; player++)
            {
                starts[player] = _symmetry.RotateFootprint(origin, YardSize, player);
            }

            return starts;
        }

        private void PaintLanes(Layout layout)
        {
            var nextStart = _symmetry.Rotate(layout.Start, 1);
            Lane(layout.Start, layout.Expansion, layout.LaneWidth + 1);
            Lane(layout.Expansion, layout.Centre, layout.LaneWidth);
            Lane(layout.Start, layout.Flank, layout.LaneWidth);
            Lane(layout.Flank, nextStart, layout.LaneWidth);
        }

        private void Lane(Cell from, Cell to, int width)
        {
            // Bresenham through the cells, stamping a disc of the lane's width at each step.
            var radius = width / 2;
            var dx = Abs(to.X - from.X);
            var dy = -Abs(to.Y - from.Y);
            var sx = from.X < to.X ? 1 : -1;
            var sy = from.Y < to.Y ? 1 : -1;
            var error = dx + dy;
            var x = from.X;
            var y = from.Y;
            while (true)
            {
                Disc(new Cell(x, y), radius, cell => _lane[Index(cell)] = true);
                if (x == to.X && y == to.Y)
                {
                    break;
                }

                var twice = 2 * error;
                if (twice >= dy)
                {
                    error += dy;
                    x += sx;
                }

                if (twice <= dx)
                {
                    error += dx;
                    y += sy;
                }
            }
        }

        // ---- heights --------------------------------------------------------------------------------

        private void PaintHeights(Layout layout)
        {
            var elevation = _settings[MapSlider.Elevation];
            var levels = _plain || elevation < 15 ? 1 : elevation < 45 ? 2 : elevation < 75 ? 3 : 4;
            if (levels > 1)
            {
                var raw = SymmetricNoise(0x4E1, _size / 4, 3);
                var thresholds = Percentiles(raw, levels);
                for (var i = 0; i < raw.Length; i++)
                {
                    var level = 0;
                    while (level < levels - 1 && raw[i] >= thresholds[level])
                    {
                        level++;
                    }

                    _level[i] = level;
                }
            }

            // Flat ground for bases, expansions, flanks and the centre, at each node's own level.
            Pad(layout.Start, layout.PlateauRadius, LevelAt(layout.Start));
            Pad(layout.BaseField, FieldRadius() + 2, LevelAt(layout.Start));
            Pad(layout.Expansion, 5, LevelAt(layout.Expansion));
            Pad(layout.Flank, 4, LevelAt(layout.Flank));
            Pad(layout.Centre, 6, LevelAt(layout.Centre));
        }

        private int LevelAt(Cell cell) => _level[Index(cell)];

        private void Pad(Cell centre, int radius, int level) => Disc(centre, radius, cell =>
        {
            var index = Index(cell);
            _pad[index] = true;
            _level[index] = level;
        });

        // ---- water ----------------------------------------------------------------------------------

        private void PaintWater(Layout layout)
        {
            if (_archetype.Rivers)
            {
                PaintRivers(layout);
            }

            if (_archetype.Lakes && LakeShare(_settings) > 0)
            {
                // The share is of the open ground: lanes and pads never flood, so they don't count.
                var raw = SymmetricNoise(0x1A4E, _size / 5, 3);
                var open = raw.Where((_, i) => !_lane[i] && !_pad[i]).ToArray();
                var threshold = Percentile(open.Length > 0 ? open : raw, LakeShare(_settings));
                for (var i = 0; i < raw.Length; i++)
                {
                    if (raw[i] < threshold && !_lane[i] && !_pad[i])
                    {
                        _terrain[i] = Terrain.Water;
                        _level[i] = 0;
                    }
                }
            }
        }

        private void PaintRivers(Layout layout)
        {
            var width = 2 + (_settings[MapSlider.Water] * 4 / 100);
            var amplitude = _size / 20;
            var meander = new ValueNoise(_seed ^ 0x51EE, _size / 3, 2);

            // An odd offset function, o(-t) = -o(t), keeps each river symmetric under the half turn.
            int Offset(int t) => ((meander.Sample(t, 0) - meander.Sample(-t, 0)) * amplitude) / 512;

            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    // Centred, doubled coordinates: the map centre is (0, 0) and every cell centre is an odd/even pair.
                    var cx = (2 * x) - Last;
                    var cy = (2 * y) - Last;
                    bool wet;
                    if (Order == 2)
                    {
                        // One river along the anti-diagonal, between the two corner bases.
                        var across = cx + cy;
                        var along = cx - cy;
                        wet = Abs(across - (2 * Offset(along / 2))) <= width + (width / 2);
                    }
                    else
                    {
                        // A cross of rivers along the midlines, one quadrant per base; the quarter turn makes the second arm.
                        wet = Abs(cx - (2 * Offset(cy / 2))) <= width || Abs(cy - (2 * Offset(-cx / 2))) <= width;
                    }

                    if (wet)
                    {
                        var index = (y * _size) + x;
                        _terrain[index] = Terrain.Water;
                        _level[index] = 0;
                    }
                }
            }

            // The centre and the flanks become islands: land, ringed with water so only bridges reach them.
            foreach (var (node, radius) in new[] { (layout.Centre, 4), (layout.Flank, 3) })
            {
                Disc(node, radius + 1 + (width / 2), cell => SetWater(cell));
                Disc(node, radius, cell =>
                {
                    var index = Index(cell);
                    _terrain[index] = Terrain.Clear;
                    _pad[index] = true;
                    _level[index] = 0;
                });
            }

            // Bases stay dry whatever the river does.
            Disc(layout.Start, layout.PlateauRadius, cell => _terrain[Index(cell)] = Terrain.Clear);
        }

        private void SetWater(Cell cell)
        {
            var index = Index(cell);
            _terrain[index] = Terrain.Water;
            _level[index] = 0;
        }

        // ---- cliffs, bridges, clutter ------------------------------------------------------------------

        private void PaintCliffs()
        {
            var cliff = new bool[_level.Length];
            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    var index = (y * _size) + x;
                    if (_terrain[index] == Terrain.Water)
                    {
                        continue;
                    }

                    // The higher side of every level change is a cliff.
                    cliff[index] = Lower(x - 1, y, index) || Lower(x + 1, y, index) || Lower(x, y - 1, index) || Lower(x, y + 1, index);
                }
            }

            for (var i = 0; i < _level.Length; i++)
            {
                if (_lane[i])
                {
                    _terrain[i] = _terrain[i] == Terrain.Water ? Terrain.Bridge : cliff[i] ? Terrain.Ramp : _terrain[i];
                }
                else if (cliff[i])
                {
                    _terrain[i] = Terrain.Rock;
                }
            }
        }

        private bool Lower(int x, int y, int index)
        {
            if (x < 0 || y < 0 || x >= _size || y >= _size)
            {
                return false;
            }

            var other = (y * _size) + x;
            return _terrain[other] != Terrain.Water && _level[other] < _level[index];
        }

        private void PaintClutter(MapSlider slider, Terrain terrain, ulong salt, int share)
        {
            if (share <= 0 || _settings[slider] == 0)
            {
                return;
            }

            // Lanes keep a one-cell margin so clutter never narrows them.
            var margin = new bool[_lane.Length];
            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    if (_lane[(y * _size) + x])
                    {
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                var nx = x + dx;
                                var ny = y + dy;
                                if (nx >= 0 && ny >= 0 && nx < _size && ny < _size)
                                {
                                    margin[(ny * _size) + nx] = true;
                                }
                            }
                        }
                    }
                }
            }

            var raw = SymmetricNoise(salt, _size / 6, 3);
            var open = raw.Where((_, i) => !margin[i] && !_pad[i]).ToArray();
            var threshold = Percentile(open.Length > 0 ? open : raw, 100 - share);
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] >= threshold && !margin[i] && !_pad[i] && _terrain[i] == Terrain.Clear)
                {
                    _terrain[i] = terrain;
                }
            }
        }

        // ---- resources --------------------------------------------------------------------------------

        private int FieldRadius() => 2 + (_settings[MapSlider.Richness] * 2 / 100);

        private void PaintFields(Layout layout)
        {
            var richness = _settings[MapSlider.Richness];
            var density = 300 + (richness * 3);
            Field(layout.BaseField, FieldRadius(), density);
            Field(layout.Expansion, FieldRadius() + 1, density + 100);
            Field(layout.Flank, FieldRadius() - 1, density);
            Field(layout.Centre, 3 + (richness * 2 / 100), density + 200);

            // Each base's Construction Yard footprint, and a ring around it, is clear ground.
            Disc(layout.Start, 2, cell =>
            {
                var index = Index(cell);
                _terrain[index] = Terrain.Clear;
                _cindrite[index] = 0;
                _fissure[index] = false;
            });
        }

        private void Field(Cell centre, int radius, int density)
        {
            // A diamond, richer in the middle, with a clear ring so harvesters can reach every cell.
            Diamond(centre, radius + 1, (cell, distance) =>
            {
                var index = Index(cell);
                if (_terrain[index] is Terrain.Rock or Terrain.Trees or Terrain.Ramp)
                {
                    _terrain[index] = Terrain.Clear;
                }

                if (distance <= radius && _terrain[index] == Terrain.Clear)
                {
                    _cindrite[index] = density + ((radius - distance) * 50);
                }
            });

            // The fissure it rises from, at its heart: the field grows back from here even when picked clean.
            Everywhere(centre, cell => _fissure[Index(cell)] = true);
        }

        // ---- painting helpers -------------------------------------------------------------------------

        // Applies an action to every cell of a disc and to all its rotations.
        private void Disc(Cell centre, int radius, Action<Cell> paint)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if ((dx * dx) + (dy * dy) <= (radius * radius) + radius)
                    {
                        Everywhere(new Cell(centre.X + dx, centre.Y + dy), paint);
                    }
                }
            }
        }

        private void Diamond(Cell centre, int radius, Action<Cell, int> paint)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var distance = Abs(dx) + Abs(dy);
                    if (distance <= radius)
                    {
                        Everywhere(new Cell(centre.X + dx, centre.Y + dy), cell => paint(cell, distance));
                    }
                }
            }
        }

        private void Everywhere(Cell cell, Action<Cell> paint)
        {
            if (!_symmetry.InBounds(cell))
            {
                return;
            }

            for (var step = 0; step < Order; step++)
            {
                paint(_symmetry.Rotate(cell, step));
            }
        }

        // Noise averaged over the cell's rotations: smooth and exactly symmetric.
        private int[] SymmetricNoise(ulong salt, int cellSize, int octaves)
        {
            var noise = new ValueNoise(_seed ^ salt, cellSize < 4 ? 4 : cellSize, octaves);
            var values = new int[_size * _size];
            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    var sum = 0;
                    for (var step = 0; step < Order; step++)
                    {
                        var turned = _symmetry.Rotate(new Cell(x, y), step);
                        sum += noise.Sample(turned.X, turned.Y);
                    }

                    values[(y * _size) + x] = sum / Order;
                }
            }

            return values;
        }

        // The value below which `percent` percent of cells fall.
        private static int Percentile(int[] values, int percent)
        {
            var sorted = values.ToArray();
            Array.Sort(sorted);
            var at = sorted.Length * percent / 100;
            return sorted[at >= sorted.Length ? sorted.Length - 1 : at];
        }

        private static int[] Percentiles(int[] values, int levels)
        {
            var thresholds = new int[levels - 1];
            for (var i = 1; i < levels; i++)
            {
                thresholds[i - 1] = Percentile(values, i * 100 / levels);
            }

            return thresholds;
        }

        private GridMap ToGrid()
        {
            var map = new GridMap(_size, _size);
            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    var cell = new Cell(x, y);
                    var index = (y * _size) + x;
                    map.SetTerrain(cell, _terrain[index]);
                    map.SetHeight(cell, _level[index]);
                    if (_cindrite[index] > 0)
                    {
                        map.SetCindrite(cell, _cindrite[index]);
                        if (_fissure[index])
                        {
                            map.SetFissure(cell, _cindrite[index]);
                        }
                    }
                }
            }

            return map;
        }

        private int Index(Cell cell) => (cell.Y * _size) + cell.X;

        private static int Abs(int value) => value < 0 ? -value : value;

        private static int Sign(int value) => value > 0 ? 1 : value < 0 ? -1 : 0;
    }
}
