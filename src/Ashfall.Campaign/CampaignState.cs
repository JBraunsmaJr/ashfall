using System.Globalization;
using Ashfall.Core;
using Ashfall.MapGen;
using Ashfall.Simulation.Horde;

namespace Ashfall.Campaign;

/// <summary>
/// A campaign in progress (GDD section 7): the generated world, which regions the team has liberated, how infested the
/// rest are, the hives still standing, active events and the global infestation meter. It advances one turn per mission
/// played, won or lost: the result is applied, the Maw spreads, events expire and new ones appear.
/// </summary>
/// <remarks>
/// A campaign is fully described by its code and the ordered list of <see cref="TurnRecord"/>s, so a save is a few
/// kilobytes and any peer can rebuild the same state from it. Each turn draws from its own random stream, seeded from
/// the campaign seed and the turn number. Integer arithmetic throughout.
/// </remarks>
public sealed class CampaignState
{
    /// <summary>The highest infestation level.</summary>
    public const int MaxInfestation = 5;

    // The turn streams ("TURN" + turn number).
    private const ulong TurnStream = 0x5455524E00000000;

    private readonly int[] _infestation;
    private readonly bool[] _liberated;
    private readonly bool[] _hive;
    private readonly int[] _attempts;
    private readonly List<CampaignEvent> _events = [];
    private readonly List<TurnRecord> _history = [];
    private int _nextEventId = 1;

    private CampaignState(CampaignCode code)
    {
        Code = code;
        Rules = code.Rules;
        RulesHash = Rules.ComputeHash();
        Difficulty = code.Difficulty;
        World = WorldGenerator.Generate(code.Seed, Rules);
        var count = World.Regions.Count;
        _infestation = new int[count];
        _liberated = new bool[count];
        _hive = new bool[count];
        _attempts = new int[count];
        foreach (var region in World.Regions)
        {
            _infestation[region.Id] = region.StartingInfestation;
            _liberated[region.Id] = region.IsHome;
            _hive[region.Id] = region.StartsAsHive;
        }

        UpdateMeter();
    }

    public CampaignCode Code { get; }

    public CampaignRules Rules { get; }

    /// <summary>The rule-set's hash, recorded in saves.</summary>
    public ulong RulesHash { get; }

    public WorldMap World { get; }

    /// <summary>The difficulty the next mission is played on. It can be lowered between turns, never raised.</summary>
    public MawDifficulty Difficulty { get; private set; }

    /// <summary>Turns played so far.</summary>
    public int Turn { get; private set; }

    public CampaignOutcome Outcome { get; private set; }

    /// <summary>The global infestation meter, 0–100: at 100 the campaign is lost.</summary>
    public int Meter { get; private set; }

    /// <summary>Survivors brought home: liberated cities and completed convoys (they pay into research in a later slice).</summary>
    public int SurvivorsRescued { get; private set; }

    /// <summary>Research rewards lost to ignored convoys (research arrives in a later slice).</summary>
    public int RewardsLost { get; private set; }

    public IReadOnlyList<CampaignEvent> Events => _events;

    public IReadOnlyList<TurnRecord> History => _history;

    /// <summary>Hives still standing.</summary>
    public int HivesLeft => _hive.Count(hive => hive);

    public int LiberatedCount => _liberated.Count(liberated => liberated);

    /// <summary>A new campaign from its code.</summary>
    public static CampaignState Start(CampaignCode code) => new(code);

    /// <summary>A new campaign: the world for <paramref name="seed"/> under <paramref name="rules"/>.</summary>
    public static CampaignState Start(uint seed, CampaignRules rules, MawDifficulty difficulty) => new(CampaignCode.For(seed, rules, difficulty));

    public int InfestationOf(int region) => _infestation[region];

    public bool IsLiberated(int region) => _liberated[region];

    public bool IsHive(int region) => _hive[region];

    public RegionOwner OwnerOf(int region) =>
        _liberated[region] ? RegionOwner.Liberated : _infestation[region] >= 3 ? RegionOwner.Maw : RegionOwner.Contested;

    /// <summary>Missions already played in a region.</summary>
    public int AttemptsAt(int region) => _attempts[region];

    /// <summary>
    /// What the team may fight for this turn: every region bordering liberated territory, then every active event.
    /// Empty once the campaign is over.
    /// </summary>
    public IReadOnlyList<CampaignTarget> Targets
    {
        get
        {
            var targets = new List<CampaignTarget>();
            if (Outcome != CampaignOutcome.InProgress)
            {
                return targets;
            }

            foreach (var region in World.Regions)
            {
                if (IsOnFront(region.Id))
                {
                    targets.Add(CampaignTarget.Region(region.Id));
                }
            }

            foreach (var campaignEvent in _events)
            {
                targets.Add(CampaignTarget.Event(campaignEvent.Id));
            }

            return targets;
        }
    }

    public CampaignEvent? FindEvent(int id) => _events.Find(campaignEvent => campaignEvent.Id == id);

    /// <summary>Whether the team may fight for <paramref name="target"/> now, and if not, why.</summary>
    public bool CanTarget(CampaignTarget target, out string reason)
    {
        reason = string.Empty;
        if (Outcome != CampaignOutcome.InProgress)
        {
            reason = "the campaign is over";
            return false;
        }

        if (target.IsEvent)
        {
            if (FindEvent(target.Id) is null)
            {
                reason = "no such event is active";
                return false;
            }

            return true;
        }

        if (target.Id < 0 || target.Id >= World.Regions.Count)
        {
            reason = "no such region";
            return false;
        }

        if (!IsOnFront(target.Id))
        {
            reason = _liberated[target.Id] ? "the region is already liberated" : "the region does not border liberated territory";
            return false;
        }

        return true;
    }

    /// <summary>The mission the team would play for <paramref name="target"/> this turn, on the current difficulty.</summary>
    public CampaignMission PlanMission(CampaignTarget target) => PlanMission(target, Difficulty);

    /// <summary>The mission for <paramref name="target"/> on <paramref name="difficulty"/> (no higher than the campaign's).</summary>
    public CampaignMission PlanMission(CampaignTarget target, MawDifficulty difficulty)
    {
        if (!CanTarget(target, out var reason))
        {
            throw new InvalidOperationException($"Cannot plan a mission for {target}: {reason}.");
        }

        var campaignEvent = target.IsEvent ? FindEvent(target.Id) : null;
        var region = World.Regions[campaignEvent?.Region ?? target.Id];
        var level = _infestation[region.Id];
        var kind = MissionKind.Purge;
        int nests;
        int tier;
        var richness = region.Richness;
        if (campaignEvent is not null)
        {
            (kind, nests, tier) = campaignEvent.Kind switch
            {
                CampaignEventKind.MeteorStrike => (MissionKind.Purge, 2, 1),
                CampaignEventKind.BehemothMigration => (MissionKind.Purge, 3, 3),
                CampaignEventKind.OutpostSiege => (MissionKind.Holdout, Rules.NestsByInfestation[3], Rules.NestTierByInfestation[3]),
                CampaignEventKind.HiveEmergence => (MissionKind.Purge, 6, 2),
                CampaignEventKind.SurvivorConvoy => (MissionKind.Escort, Rules.NestsByInfestation[2], 1),
                _ => (MissionKind.Purge, Rules.NestsByInfestation[2], 1),
            };
            if (campaignEvent.Kind == CampaignEventKind.MeteorStrike)
            {
                richness = richness + 30 > 100 ? 100 : richness + 30; // a fresh crater is rich
            }

            level = level > 3 ? level : 3; // events are fought at a front-line intensity
        }
        else if (_hive[region.Id])
        {
            kind = MissionKind.HiveAssault;
            nests = Rules.HiveNests;
            tier = 3;
        }
        else
        {
            nests = Rules.NestsByInfestation[level];
            tier = Rules.NestTierByInfestation[level];
        }

        var attempt = _attempts[region.Id];
        var seed = MissionSeed(Code.Seed, region.Id, attempt, campaignEvent is null ? 0 : (int)campaignEvent.Kind + 1);
        var archetype = region.Terrain is RegionTerrain.RiverValley or RegionTerrain.Coast ? MapArchetypes.Riverlands : MapArchetypes.Crossroads;
        int[] sliders =
        [
            kind == MissionKind.HiveAssault ? 70 : 30 + (level * 6),                                        // size
            richness,                                                                                     // richness
            region.Terrain switch { RegionTerrain.Highlands => 75, RegionTerrain.Plains => 30, RegionTerrain.Coast => 20, _ => 25 }, // elevation
            region.Terrain switch { RegionTerrain.Coast => 75, RegionTerrain.RiverValley => 45, RegionTerrain.Highlands => 10, _ => 15 }, // water
            region.Terrain == RegionTerrain.Highlands ? 60 : 35,                                          // chokepoints
            40,                                                                                           // base spacing
            region.Biome switch { Biome.Temperate => 45, Biome.Frozen => 25, Biome.Ashen => 15, _ => 10 }, // vegetation
            30 + (level * 12) > 100 ? 100 : 30 + (level * 12),                                            // fields: the Cindrite spread
            35 + (level * 10),                                                                            // regrowth
        ];
        var map = new MapSettings(archetype, (uint)(seed & uint.MaxValue), sliders);

        var liberatedNeighbours = 0;
        var survivorNeighbours = 0;
        foreach (var neighbour in region.Neighbours)
        {
            if (_liberated[neighbour])
            {
                liberatedNeighbours++;
                if (World.Regions[neighbour].Has(RegionFeatures.Survivors))
                {
                    survivorNeighbours++;
                }
            }
        }

        var credits = (liberatedNeighbours * Rules.CreditsPerLiberatedNeighbour) + (survivorNeighbours * Rules.CreditsPerSurvivorNeighbour);
        var harvesters = liberatedNeighbours / Rules.NeighboursPerHarvester;
        harvesters = harvesters > Rules.MaxBonusHarvesters ? Rules.MaxBonusHarvesters : harvesters;
        return new CampaignMission
        {
            Target = target,
            Region = region.Id,
            Event = campaignEvent?.Kind,
            Kind = kind,
            Turn = Turn + 1,
            Attempt = attempt,
            Seed = seed,
            Map = map,
            Nests = nests,
            NestTier = tier,
            Difficulty = difficulty < Difficulty ? difficulty : Difficulty,
            BonusCredits = credits,
            BonusHarvesters = harvesters,
            Briefing = Briefing(region, campaignEvent, kind, level, nests, tier, credits, harvesters, liberatedNeighbours),
        };
    }

    /// <summary>
    /// Plays one turn: applies the mission's result to its target, lets the Maw spread, expires and rolls events, and
    /// updates the meter. Returns what happened, in words, for the campaign screen.
    /// </summary>
    public TurnReport Play(TurnRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!CanTarget(record.Target, out var reason))
        {
            throw new InvalidOperationException($"Turn {Turn + 1} cannot target {record.Target}: {reason}.");
        }

        if (!Enum.IsDefined(record.Difficulty) || record.Difficulty > Difficulty)
        {
            throw new InvalidOperationException("Difficulty can be lowered between turns, never raised.");
        }

        Difficulty = record.Difficulty;
        Turn++;
        var report = new TurnReport(Turn, record.Target, record.Result.Won);
        var random = new DeterministicRandom(Code.Seed, TurnStream + (ulong)Turn);
        var standing = new List<int>();

        ApplyResult(record, report, standing);
        if (HivesLeft == 0)
        {
            Finish(record, report);
            return report;
        }

        Spread(random, report, standing);
        Expire(report);
        RollEvent(random, report);
        Finish(record, report);
        return report;
    }

    /// <summary>A hash of the whole campaign state: lobbies compare it the way they compare map and content hashes.</summary>
    public ulong ComputeHash()
    {
        var hasher = default(StateHasher);
        hasher.Add(Code.Seed);
        hasher.Add(Code.RuleSet);
        hasher.Add(RulesHash);
        hasher.Add((int)Difficulty);
        hasher.Add(Turn);
        hasher.Add((int)Outcome);
        hasher.Add(Meter);
        hasher.Add(SurvivorsRescued);
        hasher.Add(RewardsLost);
        for (var region = 0; region < _infestation.Length; region++)
        {
            hasher.Add(_infestation[region]);
            hasher.Add(_liberated[region]);
            hasher.Add(_hive[region]);
            hasher.Add(_attempts[region]);
        }

        hasher.Add(_events.Count);
        foreach (var campaignEvent in _events)
        {
            hasher.Add(campaignEvent.Id);
            hasher.Add((int)campaignEvent.Kind);
            hasher.Add(campaignEvent.Region);
            hasher.Add(campaignEvent.ExpiresAfterTurn);
        }

        hasher.Add(_nextEventId);
        return hasher.Value;
    }

    private bool IsOnFront(int region)
    {
        if (_liberated[region])
        {
            return false;
        }

        foreach (var neighbour in World.Regions[region].Neighbours)
        {
            if (_liberated[neighbour])
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyResult(TurnRecord record, TurnReport report, List<int> standing)
    {
        var campaignEvent = record.Target.IsEvent ? FindEvent(record.Target.Id) : null;
        var region = campaignEvent?.Region ?? record.Target.Id;
        var name = World.Regions[region].Name;
        _attempts[region]++;
        if (!record.Result.Won)
        {
            Raise(region, 1);
            if (record.Result.NestsStanding > 0)
            {
                standing.Add(region);
            }

            report.Add(campaignEvent is null
                ? $"The assault on {name} failed: infestation rises to {_infestation[region]}."
                : $"{EventName(campaignEvent.Kind)} at {name}: the mission failed and infestation rises to {_infestation[region]}.");
            return;
        }

        if (campaignEvent is null)
        {
            var wasHive = _hive[region];
            Liberate(region);
            report.Liberated.Add(region);

            // Taking a region pushes the Maw back from the ground around it.
            foreach (var neighbour in World.Regions[region].Neighbours)
            {
                if (!_liberated[neighbour] && !_hive[neighbour] && _infestation[neighbour] > 1)
                {
                    Lower(neighbour, Rules.PushBackLevels);
                }
            }

            report.Add(wasHive ? $"{name} liberated: the hive of the {World.Theatres[World.Regions[region].Theatre].Name} is destroyed." : $"{name} liberated.");
            if (World.Regions[region].Has(RegionFeatures.Survivors))
            {
                SurvivorsRescued++;
                report.Add($"Survivors in {name} are brought to safety.");
            }

            // Events that need the Maw to hold the region are over once it falls.
            _events.RemoveAll(other => other.Region == region && other.Kind is CampaignEventKind.BehemothMigration or CampaignEventKind.HiveEmergence);
            return;
        }

        _events.Remove(campaignEvent);
        switch (campaignEvent.Kind)
        {
            case CampaignEventKind.HiveEmergence:
                Lower(region, 1);
                report.Add($"The emerging hive at {name} is purged before it evolves.");
                break;
            case CampaignEventKind.SurvivorConvoy:
                SurvivorsRescued++;
                report.Add($"The survivor convoy from {name} reaches safety.");
                break;
            case CampaignEventKind.MeteorStrike:
                report.Add($"The meteor crater at {name} is purged before nests take root.");
                break;
            case CampaignEventKind.BehemothMigration:
                report.Add($"The behemoth column out of {name} is intercepted.");
                break;
            case CampaignEventKind.OutpostSiege:
                report.Add($"The outpost at {name} holds.");
                break;
            default:
                report.Add($"{EventName(campaignEvent.Kind)} at {name} answered.");
                break;
        }
    }

    // The Maw moves: every infested region may push into one less-infested neighbour, more likely the deeper it is, for
    // hives (more so every turn), and where a lost mission left nests standing. Decided on the levels at the start of
    // the phase, in region order, and no region is hit twice in a turn, so the order of regions never matters.
    private void Spread(DeterministicRandom random, TurnReport report, List<int> standing)
    {
        var levels = (int[])_infestation.Clone();
        var liberated = (bool[])_liberated.Clone();
        var hit = new bool[levels.Length];
        var candidates = new List<int>();
        foreach (var region in World.Regions)
        {
            var level = levels[region.Id];
            if (liberated[region.Id] || level < 1)
            {
                continue;
            }

            var chance = Rules.SpreadBasePercent + (level * Rules.SpreadPerLevelPercent) + Rules.SpreadPercent(Difficulty);
            if (_hive[region.Id])
            {
                chance += Rules.HivePushPercent + (Turn * Rules.HivePushPerTenTurnsPercent / 10);
            }

            if (standing.Contains(region.Id))
            {
                chance += Rules.NestsStandingPercent;
            }

            if (random.Next(100) >= chance)
            {
                continue;
            }

            candidates.Clear();
            foreach (var neighbour in region.Neighbours)
            {
                var neighbourLevel = liberated[neighbour] ? 0 : levels[neighbour];
                if (!hit[neighbour] && !World.Regions[neighbour].IsHome && !_hive[neighbour] && neighbourLevel < level)
                {
                    candidates.Add(neighbour);
                }
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            var target = candidates[random.Next(candidates.Count)];
            hit[target] = true;
            var name = World.Regions[target].Name;
            if (_liberated[target])
            {
                // Liberated ground is defended: half the time the push breaks on it.
                if (random.Next(2) == 0)
                {
                    report.Add($"The Maw probes {name} and is driven back.");
                    continue;
                }

                _liberated[target] = false;
                _infestation[target] = 1;
                report.Reinfested.Add(target);
                report.Add($"The Maw spreads from {region.Name} into liberated {name}.");
            }
            else
            {
                _infestation[target]++;
                report.Spread.Add(target);
                report.Add($"The infestation in {name} deepens to {_infestation[target]}.");
            }
        }
    }

    // Unanswered events whose time is up land with their consequence.
    private void Expire(TurnReport report)
    {
        for (var i = 0; i < _events.Count;)
        {
            var campaignEvent = _events[i];
            if (campaignEvent.ExpiresAfterTurn > Turn)
            {
                i++;
                continue;
            }

            _events.RemoveAt(i);
            var region = campaignEvent.Region;
            var name = World.Regions[region].Name;
            switch (campaignEvent.Kind)
            {
                case CampaignEventKind.MeteorStrike:
                    _liberated[region] = false;
                    _infestation[region] = _infestation[region] > 3 ? _infestation[region] : 3;
                    report.Add($"Nests take root in the meteor crater at {name}: infestation {_infestation[region]}.");
                    break;
                case CampaignEventKind.BehemothMigration:
                    var reached = -1;
                    foreach (var neighbour in World.Regions[region].Neighbours)
                    {
                        if (_liberated[neighbour] && !World.Regions[neighbour].IsHome)
                        {
                            reached = neighbour;
                            break;
                        }
                    }

                    if (reached >= 0)
                    {
                        _liberated[reached] = false;
                        _infestation[reached] = 2;
                        report.Reinfested.Add(reached);
                        report.Add($"The behemoth column from {name} reaches {World.Regions[reached].Name} and reinfests it.");
                    }
                    else
                    {
                        report.Add($"The behemoth column from {name} wanders off.");
                    }

                    break;
                case CampaignEventKind.OutpostSiege:
                    _liberated[region] = false;
                    _infestation[region] = _infestation[region] > 2 ? _infestation[region] : 2;
                    report.Reinfested.Add(region);
                    report.Add($"The outpost at {name} falls: the region is lost.");
                    break;
                case CampaignEventKind.HiveEmergence:
                    _liberated[region] = false;
                    _hive[region] = true;
                    _infestation[region] = MaxInfestation;
                    report.Add($"A new hive emerges at {name}.");
                    break;
                case CampaignEventKind.SurvivorConvoy:
                    RewardsLost++;
                    report.Add($"The survivor convoy from {name} is lost, and the research it carried with it.");
                    break;
                default:
                    report.Add($"{EventName(campaignEvent.Kind)} at {name} expires.");
                    break;
            }
        }
    }

    private void RollEvent(DeterministicRandom random, TurnReport report)
    {
        if (_events.Count >= Rules.MaxActiveEvents || random.Next(100) >= Rules.EventPercent(Difficulty))
        {
            return;
        }

        // Weighted among the events that may happen now and have somewhere to happen.
        var total = 0;
        var weights = new int[Rules.Events.Count];
        for (var i = 0; i < Rules.Events.Count; i++)
        {
            var rule = Rules.Events[i];
            if (rule.Weight > 0 && rule.FirstTurn <= Turn && FirstValidRegion(rule.Kind) >= 0)
            {
                weights[i] = rule.Weight;
                total += rule.Weight;
            }
        }

        if (total == 0)
        {
            return;
        }

        var roll = random.Next(total);
        var chosen = 0;
        while (roll >= weights[chosen])
        {
            roll -= weights[chosen];
            chosen++;
        }

        var kind = Rules.Events[chosen].Kind;
        var regions = new List<int>();
        foreach (var region in World.Regions)
        {
            if (IsValidEventRegion(kind, region.Id))
            {
                regions.Add(region.Id);
            }
        }

        var at = regions[random.Next(regions.Count)];
        var campaignEvent = new CampaignEvent(_nextEventId++, kind, at, Turn + Rules.Events[chosen].Lifetime);
        _events.Add(campaignEvent);
        report.NewEvents.Add(campaignEvent.Id);
        report.Add($"{EventName(kind)} at {World.Regions[at].Name}: {Rules.Events[chosen].Lifetime} turns to respond.");
    }

    private int FirstValidRegion(CampaignEventKind kind)
    {
        foreach (var region in World.Regions)
        {
            if (IsValidEventRegion(kind, region.Id))
            {
                return region.Id;
            }
        }

        return -1;
    }

    private bool IsValidEventRegion(CampaignEventKind kind, int region)
    {
        var data = World.Regions[region];
        if (data.IsHome || _events.Exists(other => other.Region == region))
        {
            return false;
        }

        switch (kind)
        {
            case CampaignEventKind.MeteorStrike:
                return !_hive[region] && (_liberated[region] || _infestation[region] <= 2);
            case CampaignEventKind.BehemothMigration:
                if (_liberated[region] || _hive[region] || _infestation[region] < 3)
                {
                    return false;
                }

                foreach (var neighbour in data.Neighbours)
                {
                    if (_liberated[neighbour] && !World.Regions[neighbour].IsHome)
                    {
                        return true;
                    }
                }

                return false;
            case CampaignEventKind.OutpostSiege:
            case CampaignEventKind.SurvivorConvoy:
                return _liberated[region];
            case CampaignEventKind.HiveEmergence:
                return !_liberated[region] && !_hive[region] && _infestation[region] >= 4 && World.Distance(World.Home, region) >= Rules.MinHiveDistance;
            default:
                return false;
        }
    }

    private void Finish(TurnRecord record, TurnReport report)
    {
        _history.Add(record);
        UpdateMeter();
        report.Meter = Meter;
        if (HivesLeft == 0)
        {
            Outcome = CampaignOutcome.Won;
            report.Add("Every hive is destroyed: the world is taken back.");
        }
        else if (Meter >= 100)
        {
            Outcome = CampaignOutcome.Lost;
            report.Add("The global infestation meter is full: the Maw has consumed the world.");
        }

        report.Outcome = Outcome;
    }

    private void UpdateMeter()
    {
        long sum = 0;
        foreach (var level in _infestation)
        {
            sum += level;
        }

        // Home never falls, so the meter is measured against every other region at the deepest infestation.
        var ceiling = (long)(_infestation.Length - 1) * MaxInfestation * Rules.MeterCeilingPercent(Difficulty);
        var meter = sum * 10_000 / ceiling;
        Meter = meter > 100 ? 100 : (int)meter;
    }

    private void Liberate(int region)
    {
        _liberated[region] = true;
        _infestation[region] = 0;
        _hive[region] = false;
    }

    private void Raise(int region, int levels)
    {
        _liberated[region] = false;
        var level = _infestation[region] + levels;
        _infestation[region] = level > MaxInfestation ? MaxInfestation : level < 1 ? 1 : level;
    }

    private void Lower(int region, int levels)
    {
        var level = _infestation[region] - levels;
        _infestation[region] = level < 1 ? 1 : level;
    }

    /// <summary>A region's mission seed: the campaign seed, region id, attempt number and event mixed (SplitMix64).</summary>
    public static ulong MissionSeed(uint campaignSeed, int region, int attempt, int salt)
    {
        var value = ((ulong)campaignSeed << 32) ^ ((ulong)(uint)region << 20) ^ ((ulong)(uint)attempt << 4) ^ (uint)salt;
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    /// <summary>An enum name in words: "RiverValley" becomes "River Valley", "Survivors, TechSite" becomes "Survivors, Tech Site".</summary>
    public static string Words(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var text = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
            {
                text.Append(' ');
            }

            text.Append(name[i]);
        }

        return text.ToString();
    }

    public static string EventName(CampaignEventKind kind) => kind switch
    {
        CampaignEventKind.MeteorStrike => "Meteor strike",
        CampaignEventKind.BehemothMigration => "Behemoth migration",
        CampaignEventKind.OutpostSiege => "Outpost under siege",
        CampaignEventKind.HiveEmergence => "Hive emergence",
        CampaignEventKind.SurvivorConvoy => "Survivor convoy",
        CampaignEventKind.KindredSchism => "Kindred schism",
        _ => "Lattice beacon",
    };

    private string Briefing(WorldRegion region, CampaignEvent? campaignEvent, MissionKind kind, int level, int nests, int tier, int credits, int harvesters, int liberatedNeighbours)
    {
        var nestName = tier switch { 3 => "Behemoth Dens", 2 => "Brood Nests", _ => "Spawning Pits" };
        var goal = kind switch
        {
            MissionKind.HiveAssault => $"Assault the hive of the {World.Theatres[region.Theatre].Name} in {region.Name}",
            MissionKind.Holdout => $"Hold the outpost in {region.Name}",
            MissionKind.Escort => $"Bring the survivors out of {region.Name}",
            _ => campaignEvent is null ? $"Purge {region.Name}" : $"{EventName(campaignEvent.Kind)}: purge {region.Name}",
        };
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{goal}. {nests} {nestName}, infestation {level}, {Words(region.Terrain.ToString()).ToLowerInvariant()}, {Words(region.Biome.ToString()).ToLowerInvariant()} biome.");
        if (liberatedNeighbours > 0)
        {
            text += string.Create(CultureInfo.InvariantCulture,
                $" {liberatedNeighbours} liberated neighbour{(liberatedNeighbours == 1 ? string.Empty : "s")}: +{credits} credits{(harvesters > 0 ? $", +{harvesters} harvester{(harvesters == 1 ? string.Empty : "s")}" : string.Empty)} each.");
        }

        return text;
    }
}

/// <summary>What one turn did, for the campaign screen and the preview tool.</summary>
public sealed class TurnReport
{
    internal TurnReport(int turn, CampaignTarget target, bool won)
    {
        Turn = turn;
        Target = target;
        Won = won;
    }

    public int Turn { get; }

    public CampaignTarget Target { get; }

    public bool Won { get; }

    /// <summary>Regions liberated this turn.</summary>
    public List<int> Liberated { get; } = [];

    /// <summary>Infested regions whose infestation deepened this turn.</summary>
    public List<int> Spread { get; } = [];

    /// <summary>Liberated regions the Maw took back this turn.</summary>
    public List<int> Reinfested { get; } = [];

    /// <summary>Events that appeared this turn.</summary>
    public List<int> NewEvents { get; } = [];

    public int Meter { get; internal set; }

    public CampaignOutcome Outcome { get; internal set; }

    /// <summary>What happened, in order, in words.</summary>
    public IReadOnlyList<string> Lines => _lines;

    private readonly List<string> _lines = [];

    internal void Add(string line) => _lines.Add(line);
}
