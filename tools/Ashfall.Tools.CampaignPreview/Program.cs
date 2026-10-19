using System.Globalization;
using System.Text;
using Ashfall.Campaign;
using Ashfall.Core;
using Ashfall.Simulation.Horde;
using Ashfall.Tools.CampaignPreview;
using Ashfall.Tools.MapPreview;

// ashfall-campaignpreview: generate campaign worlds and play whole campaigns with auto-resolved missions (GDD section
// 13, the campaign suite). Designers tune the campaign rules on these statistics; CI runs it as the campaign gate.

const string Usage = """
    ashfall-campaignpreview — campaign worlds, auto-resolved campaigns and their statistics.

    Usage: ashfall-campaignpreview [options]

    One campaign:
      --code <campaign code>   Describe this campaign (e.g. ASH-3C91K-7QZ0M)
      --turns <n>              Autopilot this many turns first (default 0)
      --skill <pct>            Autopilot skill for --turns (default 75)
      --world <file.png>       Render the world map
      --save-out <file>        Write the campaign save

    Many campaigns (when no --code is given):
      --rules <id|all>         standard, quick or all (default all)
      --difficulty <list>      recruit,veteran,elite,extinction (default all)
      --skills <list>          Autopilot skill percentages (default 40,75,90)
      --seeds <n>              Campaigns per configuration (default 50)
      --first-seed <n>         First seed (default 1)
      --min-win <pct>          Gate: on Veteran, the highest skill must win at least this often (default 90)
      --min-loss <pct>         Gate: on Veteran, the lowest skill must lose at least this often (default 80)
      --hash-out <file>        Write each campaign's final state hash (Veteran, skill 75) for cross-platform checks
      --sheet <file.png>       Render the first campaign's world after 10 autopiloted turns
      -h, --help               Show this help

    Exit codes: 0 every campaign ended (none stuck) and the gates passed, 1 otherwise, 2 bad arguments.
    """;

string? code = null;
var turns = 0;
var skill = 75;
string? worldPath = null;
string? saveOut = null;
var ruleSets = CampaignRuleSets.All.ToList();
var difficulties = Enum.GetValues<MawDifficulty>().ToList();
var skills = new List<int> { 40, 75, 90 };
var seeds = 50;
uint firstSeed = 1;
var minWin = 90;
var minLoss = 80;
string? hashOut = null;
string? sheet = null;

for (var i = 0; i < args.Length; i++)
{
    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}.");
    int Number() => int.Parse(Value(), CultureInfo.InvariantCulture);
    try
    {
        switch (args[i])
        {
            case "-h" or "--help":
                Console.WriteLine(Usage);
                return 0;
            case "--code":
                code = Value();
                break;
            case "--turns":
                turns = Number();
                break;
            case "--skill":
                skill = Number();
                break;
            case "--world":
                worldPath = Value();
                break;
            case "--save-out":
                saveOut = Value();
                break;
            case "--rules":
                var id = Value();
                ruleSets = id == "all" ? CampaignRuleSets.All.ToList() : [CampaignRuleSets.Find(id) ?? throw new ArgumentException($"Unknown rules {id}.")];
                break;
            case "--difficulty":
                difficulties = Value().Split(',').Select(name => Enum.Parse<MawDifficulty>(name, ignoreCase: true)).ToList();
                break;
            case "--skills":
                skills = Value().Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).Order().ToList();
                break;
            case "--seeds":
                seeds = Number();
                break;
            case "--first-seed":
                firstSeed = uint.Parse(Value(), CultureInfo.InvariantCulture);
                break;
            case "--min-win":
                minWin = Number();
                break;
            case "--min-loss":
                minLoss = Number();
                break;
            case "--hash-out":
                hashOut = Value();
                break;
            case "--sheet":
                sheet = Value();
                break;
            default:
                throw new ArgumentException($"Unknown option {args[i]}.");
        }
    }
    catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
    {
        Console.Error.WriteLine(error.Message);
        Console.Error.WriteLine("Run with --help for usage.");
        return 2;
    }
}

if (code is not null)
{
    if (!CampaignCode.TryParse(code, out var parsed))
    {
        Console.Error.WriteLine($"Not a campaign code: {code}");
        return 2;
    }

    var campaign = CampaignState.Start(parsed);
    var run = CampaignAutopilot.PlayToEnd(campaign, skill, parsed.Seed, turns);
    Describe(campaign);
    if (turns > 0)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Autopiloted {run.Turns} turns at skill {skill}: {run.Wins} won."));
    }

    if (worldPath is not null)
    {
        PngWriter.Write(worldPath, WorldMap.Width, WorldMap.Height, WorldImage.Render(campaign));
        Console.WriteLine($"Wrote {worldPath}");
    }

    if (saveOut is not null)
    {
        File.WriteAllText(saveOut, CampaignSave.Write(campaign));
        Console.WriteLine($"Wrote {saveOut}");
    }

    return 0;
}

var failed = false;
var hashes = new StringBuilder();
foreach (var rules in ruleSets)
{
    var sizes = new List<int>();
    var meters = new List<int>();
    for (var seed = firstSeed; seed < firstSeed + (uint)seeds; seed++)
    {
        var campaign = CampaignState.Start(seed, rules, MawDifficulty.Veteran);
        sizes.Add(campaign.World.Regions.Count);
        meters.Add(campaign.Meter);
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{rules.Name} rules ({StateHasher.Format(rules.ComputeHash())}): {sizes.Min()}-{sizes.Max()} regions (mean {sizes.Average():F1}), starting meter {meters.Min()}-{meters.Max()}% (mean {meters.Average():F0}%)"));
    Console.WriteLine("  difficulty  skill   won  lost  open stuck  turns to win (mean, range)  peak meter");
    foreach (var difficulty in difficulties)
    {
        foreach (var level in skills)
        {
            int won = 0, lost = 0, open = 0, stuck = 0;
            var winTurns = new List<int>();
            var peaks = new List<int>();
            for (var seed = firstSeed; seed < firstSeed + (uint)seeds; seed++)
            {
                var campaign = CampaignState.Start(seed, rules, difficulty);
                var run = CampaignAutopilot.PlayToEnd(campaign, level, seed);
                stuck += run.Stuck ? 1 : 0;
                peaks.Add(run.PeakMeter);
                switch (run.Outcome)
                {
                    case CampaignOutcome.Won:
                        won++;
                        winTurns.Add(run.Turns);
                        break;
                    case CampaignOutcome.Lost:
                        lost++;
                        break;
                    default:
                        open++;
                        break;
                }

                if (hashOut is not null && difficulty == MawDifficulty.Veteran && level == 75)
                {
                    hashes.Append(CultureInfo.InvariantCulture, $"{rules.Id} {seed} {run.Outcome} {run.Turns} {StateHasher.Format(campaign.ComputeHash())}\n");
                }
            }

            var turnsText = winTurns.Count == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{winTurns.Average():F0} ({winTurns.Min()}-{winTurns.Max()})");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {difficulty,-10} {level,5}% {won,5} {lost,5} {open,5} {stuck,5}  {turnsText,-26}  {peaks.Average(),3:F0}%"));

            if (stuck > 0)
            {
                Console.WriteLine($"  FAIL: {stuck} campaigns had no target to choose.");
                failed = true;
            }

            if (difficulty == MawDifficulty.Veteran && level == skills[^1] && won * 100 < minWin * seeds)
            {
                Console.WriteLine($"  FAIL: a strong team won {won} of {seeds} Veteran campaigns; the gate is {minWin}%.");
                failed = true;
            }

            if (difficulty == MawDifficulty.Veteran && level == skills[0] && skills.Count > 1 && lost * 100 < minLoss * seeds)
            {
                Console.WriteLine($"  FAIL: a weak team lost only {lost} of {seeds} Veteran campaigns; the gate is {minLoss}%.");
                failed = true;
            }
        }
    }
}

if (hashOut is not null)
{
    File.WriteAllText(hashOut, hashes.ToString());
    Console.WriteLine($"Wrote {hashOut}");
}

if (sheet is not null)
{
    var campaign = CampaignState.Start(firstSeed, ruleSets[0], MawDifficulty.Veteran);
    CampaignAutopilot.PlayToEnd(campaign, 75, firstSeed, 10);
    PngWriter.Write(sheet, WorldMap.Width, WorldMap.Height, WorldImage.Render(campaign));
    Console.WriteLine($"Wrote {sheet} ({campaign.Code} after {campaign.Turn} turns)");
}

return failed ? 1 : 0;

static void Describe(CampaignState campaign)
{
    var world = campaign.World;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{campaign.Code}: {campaign.Rules.Name} rules, {campaign.Difficulty}, {world.Regions.Count} regions in {world.Theatres.Count} theatres; turn {campaign.Turn}, meter {campaign.Meter}%, {campaign.HivesLeft} hives, {campaign.Outcome}"));
    Console.WriteLine($"  state hash {StateHasher.Format(campaign.ComputeHash())}");
    foreach (var theatre in world.Theatres)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {theatre.Name} ({theatre.Biome}, {theatre.Regions.Count} regions, hive at {world.Regions[theatre.Hive].Name}, {world.Distance(world.Home, theatre.Hive)} borders from home)"));
        foreach (var id in theatre.Regions)
        {
            var region = world.Regions[id];
            var tags = new List<string>();
            if (region.IsHome)
            {
                tags.Add("home");
            }

            if (campaign.IsHive(id))
            {
                tags.Add("HIVE");
            }

            if (region.Features != RegionFeatures.None)
            {
                tags.Add(region.Features.ToString());
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"    {id,2} {region.Name,-22} {campaign.OwnerOf(id),-10} infestation {campaign.InfestationOf(id)}  {region.Terrain,-11} {region.Biome,-9} richness {region.Richness,3}  borders {string.Join(",", region.Neighbours)}{(tags.Count > 0 ? "  [" + string.Join(", ", tags) + "]" : string.Empty)}"));
        }
    }

    foreach (var campaignEvent in campaign.Events)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  event {campaignEvent.Id}: {CampaignState.EventName(campaignEvent.Kind)} at {world.Regions[campaignEvent.Region].Name}, until turn {campaignEvent.ExpiresAfterTurn}"));
    }

    var targets = campaign.Targets;
    if (targets.Count > 0)
    {
        var mission = campaign.PlanMission(CampaignAutopilot.ChooseTarget(campaign));
        Console.WriteLine($"  next mission ({mission.Map.ToCode()}): {mission.Briefing}");
    }
}
