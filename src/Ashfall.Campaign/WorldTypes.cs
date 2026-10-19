namespace Ashfall.Campaign;

/// <summary>A region's biome (GDD section 7). Until biomes reach the map generator it sets how wooded a mission's map is.</summary>
public enum Biome
{
    Temperate,
    Arid,
    Frozen,
    Ashen,
}

/// <summary>A region's lie of the land, which picks the mission's map archetype (GDD section 7, "From region to mission").</summary>
public enum RegionTerrain
{
    /// <summary>Open ground: Crossroads.</summary>
    Plains,

    /// <summary>A river valley: Riverlands.</summary>
    RiverValley,

    /// <summary>Mountains: Canyonlands once it exists (M9); until then a high, cliff-cut Crossroads.</summary>
    Highlands,

    /// <summary>A coast: Archipelago once it exists (M9); until then a very wet Riverlands.</summary>
    Coast,
}

/// <summary>Optional features of a region (GDD section 7), each bringing something to missions there or next door.</summary>
[Flags]
public enum RegionFeatures
{
    None = 0,

    /// <summary>A city with survivors: liberating it pays, and its neighbours start with extra credits.</summary>
    Survivors = 1,

    /// <summary>A neutral tech site.</summary>
    TechSite = 2,

    /// <summary>A meteor crater: unusually rich Cindrite.</summary>
    Crater = 4,

    /// <summary>A Kindred shrine (matters once the Kindred are playable, M9).</summary>
    KindredShrine = 8,

    /// <summary>A Lattice node (matters once the Lattice is playable, M9).</summary>
    LatticeNode = 16,
}

/// <summary>Who holds a region, derived from its state: liberated by the players, contested (infestation 1–2) or the Maw's (3–5).</summary>
public enum RegionOwner
{
    Liberated,
    Contested,
    Maw,
}

/// <summary>How a campaign stands.</summary>
public enum CampaignOutcome
{
    InProgress,

    /// <summary>Every hive has been destroyed.</summary>
    Won,

    /// <summary>The global infestation meter reached 100%.</summary>
    Lost,
}

/// <summary>One region of the world map: everything about it that never changes during a campaign.</summary>
public sealed class WorldRegion
{
    internal WorldRegion(int id, int theatre, int x, int y)
    {
        Id = id;
        Theatre = theatre;
        X = x;
        Y = y;
    }

    public int Id { get; }

    public string Name { get; internal set; } = string.Empty;

    /// <summary>The theatre (group of regions with one hive) this region belongs to.</summary>
    public int Theatre { get; internal set; }

    /// <summary>Position on the world map, 0..<see cref="WorldMap.Width"/> by 0..<see cref="WorldMap.Height"/>.</summary>
    public int X { get; }

    public int Y { get; }

    public Biome Biome { get; internal set; }

    public RegionTerrain Terrain { get; internal set; }

    /// <summary>How much Cindrite the region holds, 0–100: the mission's richness slider.</summary>
    public int Richness { get; internal set; }

    public RegionFeatures Features { get; internal set; }

    /// <summary>Regions sharing a border with this one, in ascending id order.</summary>
    public IReadOnlyList<int> Neighbours { get; internal set; } = [];

    /// <summary>Infestation at the start of the campaign, 0–5.</summary>
    public int StartingInfestation { get; internal set; }

    /// <summary>Whether the region holds its theatre's hive at the start of the campaign.</summary>
    public bool StartsAsHive { get; internal set; }

    /// <summary>The team's liberated home region.</summary>
    public bool IsHome { get; internal set; }

    public bool Has(RegionFeatures feature) => (Features & feature) == feature;
}

/// <summary>A theatre: a group of neighbouring regions sharing a biome, with one hive to destroy.</summary>
public sealed class Theatre
{
    internal Theatre(int id, string name, Biome biome, IReadOnlyList<int> regions, int hive)
    {
        Id = id;
        Name = name;
        Biome = biome;
        Regions = regions;
        Hive = hive;
    }

    public int Id { get; }

    public string Name { get; }

    public Biome Biome { get; }

    /// <summary>The theatre's regions, in ascending id order.</summary>
    public IReadOnlyList<int> Regions { get; }

    /// <summary>The region holding the theatre's hive at the start of the campaign.</summary>
    public int Hive { get; }
}

/// <summary>A generated world: regions, borders and theatres. Fixed for the whole campaign.</summary>
public sealed class WorldMap
{
    /// <summary>Width of the world map's coordinate space.</summary>
    public const int Width = 1000;

    /// <summary>Height of the world map's coordinate space.</summary>
    public const int Height = 640;

    private readonly int[][] _distances;

    internal WorldMap(IReadOnlyList<WorldRegion> regions, IReadOnlyList<Theatre> theatres, int home)
    {
        Regions = regions;
        Theatres = theatres;
        Home = home;
        _distances = new int[regions.Count][];
        for (var region = 0; region < regions.Count; region++)
        {
            _distances[region] = BreadthFirst(regions, region);
        }
    }

    public IReadOnlyList<WorldRegion> Regions { get; }

    public IReadOnlyList<Theatre> Theatres { get; }

    /// <summary>The team's home region.</summary>
    public int Home { get; }

    /// <summary>Borders, each once, as (lower id, higher id), in order.</summary>
    public IEnumerable<(int A, int B)> Borders
    {
        get
        {
            foreach (var region in Regions)
            {
                foreach (var neighbour in region.Neighbours)
                {
                    if (neighbour > region.Id)
                    {
                        yield return (region.Id, neighbour);
                    }
                }
            }
        }
    }

    /// <summary>Borders crossed on the shortest way from one region to another.</summary>
    public int Distance(int from, int to) => _distances[from][to];

    public bool AreNeighbours(int a, int b)
    {
        foreach (var neighbour in Regions[a].Neighbours)
        {
            if (neighbour == b)
            {
                return true;
            }
        }

        return false;
    }

    internal static int[] BreadthFirst(IReadOnlyList<WorldRegion> regions, int start)
    {
        var distance = new int[regions.Count];
        Array.Fill(distance, -1);
        distance[start] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var region = queue.Dequeue();
            foreach (var neighbour in regions[region].Neighbours)
            {
                if (distance[neighbour] < 0)
                {
                    distance[neighbour] = distance[region] + 1;
                    queue.Enqueue(neighbour);
                }
            }
        }

        return distance;
    }
}
