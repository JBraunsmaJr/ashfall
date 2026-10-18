using System.Diagnostics;
using System.Globalization;
using Ashfall.MapGen;
using Ashfall.Tools.MapPreview;

// ashfall-mappreview: generate maps in bulk and report how they score (GDD section 6, "Tooling").
// Designers tune rule-sets on these statistics rather than single samples; CI runs it as the M4 gate.

const string Usage = """
    ashfall-mappreview — bulk map generation, validation statistics and contact sheets.

    Usage: ashfall-mappreview [options]

      --archetype <id|all>     crossroads, riverlands or all (default all)
      --players <list>         Player counts, e.g. 2,3,4 (default 2,3,4)
      --seeds <n>              Seeds per configuration (default 50)
      --first-seed <n>         First seed (default 1)
      --slider <name=value>    Fix a slider (size, richness, elevation, water, chokepoints, basespacing,
                               vegetation); repeatable
      --sweep                  Also run every slider at 0, 25, 50, 75 and 100 (others at defaults)
      --code <map code>        Generate just this map code (with --players)
      --verbose                Also list why attempts were rejected and retried
      --sheet <file.png>       Render a contact sheet of the first maps generated
      --sheet-count <n>        Maps on the sheet (default 16)
      --min-first-try <pct>    Fail (exit 1) if fewer maps than this pass on the first attempt (default 80)
      -h, --help               Show this help

    Exit codes: 0 every map valid and the first-try rate met, 1 otherwise, 2 bad arguments.
    """;

var archetypes = MapArchetypes.All.ToList();
var players = new List<int> { 2, 3, 4 };
var seeds = 50;
uint firstSeed = 1;
var fixedSliders = new Dictionary<MapSlider, int>();
var sweep = false;
var verbose = false;
string? code = null;
string? sheetPath = null;
var sheetCount = 16;
var minFirstTry = 80;

for (var i = 0; i < args.Length; i++)
{
    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}.");
    try
    {
        switch (args[i])
        {
            case "-h" or "--help":
                Console.WriteLine(Usage);
                return 0;
            case "--archetype":
                var id = Value();
                archetypes = id == "all" ? MapArchetypes.All.ToList() : [MapArchetypes.Find(id) ?? throw new ArgumentException($"Unknown archetype {id}.")];
                break;
            case "--players":
                players = Value().Split(',').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToList();
                break;
            case "--seeds":
                seeds = int.Parse(Value(), CultureInfo.InvariantCulture);
                break;
            case "--first-seed":
                firstSeed = uint.Parse(Value(), CultureInfo.InvariantCulture);
                break;
            case "--slider":
                var parts = Value().Split('=');
                var slider = Enum.GetValues<MapSlider>().First(s => string.Equals(s.ToString(), parts[0], StringComparison.OrdinalIgnoreCase));
                fixedSliders[slider] = int.Parse(parts[1], CultureInfo.InvariantCulture);
                break;
            case "--verbose":
                verbose = true;
                break;
            case "--sweep":
                sweep = true;
                break;
            case "--code":
                code = Value();
                break;
            case "--sheet":
                sheetPath = Value();
                break;
            case "--sheet-count":
                sheetCount = int.Parse(Value(), CultureInfo.InvariantCulture);
                break;
            case "--min-first-try":
                minFirstTry = int.Parse(Value(), CultureInfo.InvariantCulture);
                break;
            default:
                throw new ArgumentException($"Unknown option {args[i]}.");
        }
    }
    catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or OverflowException)
    {
        Console.Error.WriteLine(e.Message);
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

if (players.Any(p => p is < MapGenerator.MinPlayers or > MapGenerator.MaxPlayers))
{
    Console.Error.WriteLine("Player counts must be 2 to 4.");
    return 2;
}

// Every configuration to run: (label, settings for a seed).
var configurations = new List<(string Label, Func<uint, MapSettings> Make)>();
if (code is not null)
{
    if (!MapSettings.TryParseCode(code, out var parsed))
    {
        Console.Error.WriteLine($"Not a valid map code: {code}");
        return 2;
    }

    configurations.Add((parsed.ToCode(), _ => parsed));
    seeds = 1;
}
else
{
    foreach (var archetype in archetypes)
    {
        configurations.Add((archetype.Name, seed => Apply(new MapSettings(archetype, seed))));
        if (sweep)
        {
            foreach (var slider in Enum.GetValues<MapSlider>())
            {
                foreach (var value in new[] { 0, 25, 50, 75, 100 })
                {
                    var (s, v) = (slider, value);
                    configurations.Add(($"{archetype.Name} {s}={v}", seed => Apply(new MapSettings(archetype, seed)).With(s, v)));
                }
            }
        }
    }
}

MapSettings Apply(MapSettings settings)
{
    foreach (var slider in Enum.GetValues<MapSlider>())
    {
        if (fixedSliders.TryGetValue(slider, out var value))
        {
            settings = settings.With(slider, value);
        }
    }

    return settings;
}

var sheet = new List<GeneratedMap>();
var totalMaps = 0;
var totalFirstTry = 0;
var totalInvalid = 0;
Console.WriteLine($"{"configuration",-34} {"pl",2} {"maps",5} {"1st try",7} {"att",5} {"fallbk",6} {"fair min",8} {"fair avg",8} {"size",4} {"ms avg",7}");
foreach (var (label, make) in configurations)
{
    foreach (var count in players)
    {
        var maps = 0;
        var firstTry = 0;
        var attempts = 0;
        var fallbacks = 0;
        var invalid = 0;
        var minScore = 100;
        long scoreSum = 0;
        var size = 0;
        var clock = Stopwatch.StartNew();
        var failures = new Dictionary<string, int>();
        var rejections = new Dictionary<string, int>();
        for (var i = 0; i < seeds; i++)
        {
            var settings = make(firstSeed + (uint)i);
            var generated = MapGenerator.Generate(settings, count);
            maps++;
            attempts += generated.Attempts;
            size = generated.Size;
            if (generated.Attempts == 1 && !generated.FellBack)
            {
                firstTry++;
            }

            if (generated.FellBack)
            {
                fallbacks++;
            }

            foreach (var reason in generated.Rejected)
            {
                rejections[reason] = rejections.GetValueOrDefault(reason) + 1;
            }

            if (!generated.Report.Valid)
            {
                invalid++;
                foreach (var failure in generated.Report.Failures)
                {
                    failures[failure] = failures.GetValueOrDefault(failure) + 1;
                }
            }

            minScore = Math.Min(minScore, generated.Report.FairnessScore);
            scoreSum += generated.Report.FairnessScore;
            if (sheet.Count < sheetCount)
            {
                sheet.Add(generated);
            }

            if (code is not null)
            {
                Describe(generated);
            }
        }

        totalMaps += maps;
        totalFirstTry += firstTry;
        totalInvalid += invalid;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Trim(label, 34),-34} {count,2} {maps,5} {firstTry * 100 / maps,6}% {(double)attempts / maps,5:F2} {fallbacks,6} {minScore,8} {(double)scoreSum / maps,8:F1} {size,4} {clock.Elapsed.TotalMilliseconds / maps,7:F1}"));
        foreach (var (failure, times) in failures.OrderByDescending(f => f.Value))
        {
            Console.WriteLine($"    INVALID x{times}: {failure}");
        }

        if (verbose)
        {
            foreach (var (reason, times) in rejections.OrderByDescending(f => f.Value).Take(5))
            {
                Console.WriteLine($"    retried x{times}: {reason}");
            }
        }
    }
}

var rate = totalMaps == 0 ? 0 : totalFirstTry * 100 / totalMaps;
Console.WriteLine($"{totalMaps} maps: {rate}% valid on the first attempt, {totalInvalid} invalid after fallback.");

if (sheetPath is not null && sheet.Count > 0)
{
    WriteSheet(sheetPath, sheet);
    Console.WriteLine($"Wrote {sheet.Count} maps to {sheetPath}.");
}

if (totalInvalid > 0 || rate < minFirstTry)
{
    Console.Error.WriteLine($"FAILED: {totalInvalid} invalid maps; first-try rate {rate}% (needs {minFirstTry}%).");
    return 1;
}

return 0;

static void Describe(GeneratedMap generated)
{
    var report = generated.Report;
    Console.WriteLine($"{generated.Effective.ToCode()} {generated.Size}x{generated.Size}, {generated.Players} players, symmetry {generated.SymmetryOrder}, attempts {generated.Attempts}{(generated.FellBack ? " (fallback)" : string.Empty)}, hash 0x{generated.Hash:X16}");
    Console.WriteLine($"  fairness {report.FairnessScore}; paths {string.Join("/", report.PathToNearestEnemy)}; home Cindrite {string.Join("/", report.HomeResources)}; buildable {string.Join("/", report.BuildableArea)}; reachable {report.ReachableResources / 10}%");
    foreach (var clamp in generated.Clamps)
    {
        Console.WriteLine("  limited: " + clamp);
    }

    foreach (var failure in report.Failures)
    {
        Console.WriteLine("  FAIL: " + failure);
    }
}

static void WriteSheet(string path, List<GeneratedMap> maps)
{
    const int Scale = 3;
    const int Gap = 8;
    var columns = Math.Min(4, maps.Count);
    var rows = (maps.Count + columns - 1) / columns;
    var cell = (maps.Max(m => m.Size) * Scale) + Gap;
    var width = (columns * cell) + Gap;
    var height = (rows * cell) + Gap;
    var rgb = new byte[width * height * 3];
    MapImage.Fill(rgb, width, 0, 0, width, height, (16, 16, 20));
    for (var i = 0; i < maps.Count; i++)
    {
        MapImage.Paint(rgb, width, Gap + (i % columns * cell), Gap + (i / columns * cell), Scale, maps[i]);
    }

    PngWriter.Write(path, width, height, rgb);
}

static string Trim(string text, int length) => text.Length <= length ? text : text[..length];
