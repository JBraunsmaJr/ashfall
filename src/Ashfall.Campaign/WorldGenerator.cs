using Ashfall.Core;

namespace Ashfall.Campaign;

/// <summary>
/// Generates a campaign's world from its seed and rule-set (GDD section 7, "World generation"): regions scattered over
/// the map with a minimum spacing, borders between natural neighbours (a Gabriel graph, so the map is connected and no
/// borders cross), theatres grown outwards from far-apart seeds so each is one connected piece, a home region near the
/// middle, one hive per theatre as far from home as the theatre allows, and infestation that is light around home and
/// deep around the hives. Integer arithmetic throughout: every peer generates the same world.
/// </summary>
public static class WorldGenerator
{
    // The world generator's random stream ("WRLD"); each retry uses the next stream.
    private const ulong Stream = 0x57524C44;

    private const int MaxAttempts = 64;

    // Regions keep this far from the edge of the world map.
    private const int Margin = 40;

    private static readonly string[] Syllables =
        ["ka", "ves", "tor", "mir", "dun", "ral", "esh", "bel", "cor", "ny", "os", "tha", "gar", "lin", "sev", "um", "ark", "dra", "fen", "hal", "ost", "ri", "vel", "zan"];

    /// <summary>The world for <paramref name="seed"/> under <paramref name="rules"/>.</summary>
    public static WorldMap Generate(uint seed, CampaignRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var random = new DeterministicRandom(seed, Stream + (ulong)attempt);
            if (TryGenerate(random, rules) is { } world)
            {
                return world;
            }
        }

        throw new InvalidOperationException($"No valid world for seed {seed} under the {rules.Id} rules after {MaxAttempts} attempts.");
    }

    private static WorldMap? TryGenerate(DeterministicRandom random, CampaignRules rules)
    {
        var count = rules.MinRegions + random.Next(rules.MaxRegions - rules.MinRegions + 1);
        var points = Scatter(random, count);
        var neighbours = GabrielGraph(points);

        var theatreCount = rules.MinTheatres + random.Next(rules.MaxTheatres - rules.MinTheatres + 1);
        if (theatreCount * rules.MinRegionsPerTheatre > count)
        {
            theatreCount = count / rules.MinRegionsPerTheatre;
        }

        var theatreOf = GrowTheatres(random, points, neighbours, theatreCount);
        var members = new List<int>[theatreCount];
        for (var theatre = 0; theatre < theatreCount; theatre++)
        {
            members[theatre] = [];
        }

        for (var region = 0; region < count; region++)
        {
            members[theatreOf[region]].Add(region);
        }

        foreach (var list in members)
        {
            if (list.Count < rules.MinRegionsPerTheatre)
            {
                return null;
            }
        }

        var regions = new WorldRegion[count];
        for (var i = 0; i < count; i++)
        {
            regions[i] = new WorldRegion(i, theatreOf[i], points[i].X, points[i].Y) { Neighbours = neighbours[i] };
        }

        // Home: one of the three regions nearest the middle of the map.
        var byCentre = Enumerable.Range(0, count)
            .OrderBy(i => DistanceSquared(points[i], (WorldMap.Width / 2, WorldMap.Height / 2)))
            .ThenBy(i => i)
            .ToArray();
        var home = byCentre[random.Next(3 < count ? 3 : count)];
        regions[home].IsHome = true;
        var fromHome = WorldMap.BreadthFirst(regions, home);

        // Each theatre's hive: its region farthest from home (ties: the lower id).
        var hives = new int[theatreCount];
        for (var theatre = 0; theatre < theatreCount; theatre++)
        {
            var best = -1;
            foreach (var region in members[theatre])
            {
                if (region != home && (best < 0 || fromHome[region] > fromHome[best]))
                {
                    best = region;
                }
            }

            if (best < 0 || fromHome[best] < rules.MinHiveDistance)
            {
                return null;
            }

            hives[theatre] = best;
            regions[best].StartsAsHive = true;
        }

        var fromHives = new int[count];
        Array.Fill(fromHives, int.MaxValue);
        foreach (var hive in hives)
        {
            var distances = WorldMap.BreadthFirst(regions, hive);
            for (var region = 0; region < count; region++)
            {
                fromHives[region] = distances[region] < fromHives[region] ? distances[region] : fromHives[region];
            }
        }

        // Biomes: one per theatre, all different while they last.
        var biomes = Enum.GetValues<Biome>();
        Shuffle(random, biomes);
        var theatreBiome = new Biome[theatreCount];
        for (var theatre = 0; theatre < theatreCount; theatre++)
        {
            theatreBiome[theatre] = biomes[theatre % biomes.Length];
        }

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var region = regions[i];
            region.StartingInfestation = region.IsHome ? 0
                : region.StartsAsHive ? 5
                : StartingInfestation(random, fromHome[i], fromHives[i]);
            region.Biome = random.Next(100) < 15 ? biomes[random.Next(biomes.Length)] : theatreBiome[region.Theatre];
            region.Terrain = PickTerrain(random, points[i]);
            region.Richness = 35 + random.Next(41);
            if (!region.IsHome && !region.StartsAsHive)
            {
                region.Features = RollFeatures(random, rules);
            }

            if (region.Has(RegionFeatures.Crater))
            {
                region.Richness = region.Richness + 25 > 100 ? 100 : region.Richness + 25;
            }

            if (region.StartsAsHive)
            {
                region.Richness = 80 + random.Next(21);
            }

            region.Name = UniqueName(random, region.Terrain, usedNames);
        }

        var theatres = new Theatre[theatreCount];
        for (var theatre = 0; theatre < theatreCount; theatre++)
        {
            theatres[theatre] = new Theatre(theatre, TheatreName(members[theatre], points, theatreBiome[theatre]), theatreBiome[theatre], members[theatre], hives[theatre]);
        }

        return new WorldMap(regions, theatres, home);
    }

    // The home ring is lightly infested; infestation deepens with distance from home and towards the hives, but most of
    // the world starts contested (1–2): the deep infestation is what grows around the hives as the campaign goes on.
    private static int StartingInfestation(DeterministicRandom random, int fromHome, int fromHive)
    {
        var ramp = fromHome <= 2 ? 1 : fromHome == 3 ? 2 : 2 + random.Next(2);
        var nearHive = 4 - fromHive; // a hive's neighbours start at 3
        var level = ramp > nearHive ? ramp : nearHive;
        var cap = fromHome <= 1 ? 2 : 4; // home's own ring stays light
        return level < 1 ? 1 : level > cap ? cap : level;
    }

    // Points scattered over the world with a minimum spacing, relaxed whenever it cannot be met.
    private static (int X, int Y)[] Scatter(DeterministicRandom random, int count)
    {
        var spacing = ISqrt((long)WorldMap.Width * WorldMap.Height / count) * 72 / 100;
        var points = new List<(int X, int Y)>(count);
        var tries = 0;
        while (points.Count < count)
        {
            var candidate = (Margin + random.Next(WorldMap.Width - (2 * Margin)), Margin + random.Next(WorldMap.Height - (2 * Margin)));
            var fits = true;
            foreach (var point in points)
            {
                if (DistanceSquared(point, candidate) < (long)spacing * spacing)
                {
                    fits = false;
                    break;
                }
            }

            if (fits)
            {
                points.Add(candidate);
            }
            else if (++tries > 2_000)
            {
                spacing = spacing * 9 / 10;
                tries = 0;
            }
        }

        return [.. points];
    }

    // A border between two regions when no third region lies inside the circle on them as a diameter.
    private static int[][] GabrielGraph((int X, int Y)[] points)
    {
        var lists = new List<int>[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            lists[i] = [];
        }

        for (var a = 0; a < points.Length; a++)
        {
            for (var b = a + 1; b < points.Length; b++)
            {
                var blocked = false;
                for (var c = 0; c < points.Length && !blocked; c++)
                {
                    if (c == a || c == b)
                    {
                        continue;
                    }

                    // c lies strictly inside the circle on a-b as a diameter iff (c - a) . (c - b) < 0.
                    var dot = ((long)(points[c].X - points[a].X) * (points[c].X - points[b].X))
                        + ((long)(points[c].Y - points[a].Y) * (points[c].Y - points[b].Y));
                    blocked = dot < 0;
                }

                if (!blocked)
                {
                    lists[a].Add(b);
                    lists[b].Add(a);
                }
            }
        }

        return lists.Select(list => list.Order().ToArray()).ToArray();
    }

    // Theatres grow across borders, one region at a time in turn, from seeds spread as far apart as possible, so every
    // theatre is one connected piece of the map.
    private static int[] GrowTheatres(DeterministicRandom random, (int X, int Y)[] points, int[][] neighbours, int theatres)
    {
        var count = points.Length;
        var seeds = new List<int> { random.Next(count) };
        while (seeds.Count < theatres)
        {
            var best = -1;
            var bestDistance = -1L;
            for (var region = 0; region < count; region++)
            {
                var nearest = long.MaxValue;
                foreach (var seed in seeds)
                {
                    var distance = DistanceSquared(points[region], points[seed]);
                    nearest = distance < nearest ? distance : nearest;
                }

                if (nearest > bestDistance)
                {
                    best = region;
                    bestDistance = nearest;
                }
            }

            seeds.Add(best);
        }

        var owner = new int[count];
        Array.Fill(owner, -1);
        var frontiers = new Queue<int>[theatres];
        for (var theatre = 0; theatre < theatres; theatre++)
        {
            owner[seeds[theatre]] = theatre;
            frontiers[theatre] = new Queue<int>();
            frontiers[theatre].Enqueue(seeds[theatre]);
        }

        var claimed = theatres;
        while (claimed < count)
        {
            var grew = false;
            for (var theatre = 0; theatre < theatres; theatre++)
            {
                // Claim one unclaimed neighbour of the theatre's frontier, nearest the theatre's seed first.
                while (frontiers[theatre].Count > 0)
                {
                    var from = frontiers[theatre].Peek();
                    var next = -1;
                    foreach (var neighbour in neighbours[from])
                    {
                        if (owner[neighbour] < 0
                            && (next < 0 || DistanceSquared(points[neighbour], points[seeds[theatre]]) < DistanceSquared(points[next], points[seeds[theatre]])))
                        {
                            next = neighbour;
                        }
                    }

                    if (next < 0)
                    {
                        frontiers[theatre].Dequeue();
                        continue;
                    }

                    owner[next] = theatre;
                    frontiers[theatre].Enqueue(next);
                    claimed++;
                    grew = true;
                    break;
                }
            }

            if (!grew)
            {
                break; // a disconnected leftover (cannot happen in a Gabriel graph, which is connected)
            }
        }

        return owner;
    }

    private static RegionTerrain PickTerrain(DeterministicRandom random, (int X, int Y) point)
    {
        var nearEdge = point.X < 120 || point.X > WorldMap.Width - 120 || point.Y < 100 || point.Y > WorldMap.Height - 100;
        var coast = nearEdge ? 40 : 10;
        var roll = random.Next(35 + 30 + 20 + coast);
        return roll < 35 ? RegionTerrain.Plains
            : roll < 65 ? RegionTerrain.RiverValley
            : roll < 85 ? RegionTerrain.Highlands
            : RegionTerrain.Coast;
    }

    private static RegionFeatures RollFeatures(DeterministicRandom random, CampaignRules rules)
    {
        var features = RegionFeatures.None;
        if (random.Next(100) < rules.SurvivorsPercent)
        {
            features |= RegionFeatures.Survivors;
        }

        if (random.Next(100) < rules.TechSitePercent)
        {
            features |= RegionFeatures.TechSite;
        }

        if (random.Next(100) < rules.CraterPercent)
        {
            features |= RegionFeatures.Crater;
        }

        if (random.Next(100) < rules.KindredShrinePercent)
        {
            features |= RegionFeatures.KindredShrine;
        }

        if (random.Next(100) < rules.LatticeNodePercent)
        {
            features |= RegionFeatures.LatticeNode;
        }

        return features;
    }

    private static string UniqueName(DeterministicRandom random, RegionTerrain terrain, HashSet<string> used)
    {
        var suffix = terrain switch
        {
            RegionTerrain.RiverValley => "Vale",
            RegionTerrain.Highlands => "Heights",
            RegionTerrain.Coast => "Coast",
            _ => "Flats",
        };

        while (true)
        {
            var stem = Syllables[random.Next(Syllables.Length)] + Syllables[random.Next(Syllables.Length)];
            if (random.Next(3) == 0)
            {
                stem += Syllables[random.Next(Syllables.Length)];
            }

            var name = char.ToUpperInvariant(stem[0]) + stem[1..] + " " + suffix;
            if (used.Add(name))
            {
                return name;
            }
        }
    }

    // "Northern Wastes", "Central Reaches": where the theatre lies on the map and what it is like.
    private static string TheatreName(List<int> members, (int X, int Y)[] points, Biome biome)
    {
        long x = 0, y = 0;
        foreach (var region in members)
        {
            x += points[region].X;
            y += points[region].Y;
        }

        x /= members.Count;
        y /= members.Count;
        var dx = x - (WorldMap.Width / 2);
        var dy = (y - (WorldMap.Height / 2)) * WorldMap.Width / WorldMap.Height;
        var place = (dx < 0 ? -dx : dx) < 120 && (dy < 0 ? -dy : dy) < 120 ? "Central"
            : (dx < 0 ? -dx : dx) > (dy < 0 ? -dy : dy) ? dx < 0 ? "Western" : "Eastern"
            : dy < 0 ? "Northern" : "Southern";
        var land = biome switch
        {
            Biome.Arid => "Wastes",
            Biome.Frozen => "Tundra",
            Biome.Ashen => "Ashlands",
            _ => "Reaches",
        };
        return place + " " + land;
    }

    private static void Shuffle<T>(DeterministicRandom random, T[] items)
    {
        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static long DistanceSquared((int X, int Y) a, (int X, int Y) b)
    {
        long dx = a.X - b.X;
        long dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    internal static int ISqrt(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var root = 0L;
        var bit = 1L << 62;
        while (bit > value)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (value >= root + bit)
            {
                value -= root + bit;
                root = (root >> 1) + bit;
            }
            else
            {
                root >>= 1;
            }

            bit >>= 2;
        }

        return (int)root;
    }
}
