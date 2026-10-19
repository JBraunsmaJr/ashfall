using Ashfall.Core;
using Ashfall.MapGen;
using Ashfall.Session.Maps;
using Ashfall.Simulation;
using Ashfall.Simulation.Horde;

namespace Ashfall.Session;

/// <summary>
/// Everything needed to start a match identically on every peer: the M0 "proving ground" (open map,
/// wandering units, used to prove determinism), the skirmish (2–4 Bulwark players on a hand-made or
/// generated map, any mix of people and AIs, in teams, played to victory), the swarm stress test, and
/// the co-op Purge against the Maw. AI tiers join the spec in a later milestone.
/// </summary>
public sealed record MatchSpec
{
    /// <summary>Default skirmish tick limit: 60 minutes of game time. A match still running then is a failure.</summary>
    public const long SkirmishTickLimit = 54_000;

    /// <summary>Creatures in the swarm stress test unless told otherwise: the GDD's 5,000-creature target.</summary>
    public const int SwarmDefaultCreatures = 5_000;

    /// <summary>Most creatures a swarm match may ask for.</summary>
    public const int SwarmMaxCreatures = 20_000;

    /// <summary>Most nests a swarm match may ask for.</summary>
    public const int SwarmMaxNests = 8;

    /// <summary>Nests a Purge starts with unless told otherwise.</summary>
    public const int PurgeDefaultNests = 4;

    /// <summary>Most nests a Purge may ask for.</summary>
    public const int PurgeMaxNests = 12;

    /// <summary>The most Maw creatures alive at once in a Purge unless told otherwise.</summary>
    public const int PurgeDefaultCreatures = 1_500;

    /// <summary>
    /// Ticks before a Purge's nests start breeding (3 minutes): time to build a refinery and barracks. The nests feed
    /// all the while, so the first brood is a big one.
    /// </summary>
    public const long PurgeGraceTicks = 2_700;

    /// <summary>
    /// Ticks between the Maw's waves in a Purge (2 minutes). The first goes one interval after the grace period; in
    /// between, what the nests breed broods around them.
    /// </summary>
    public const int PurgeWaveIntervalTicks = 1_800;

    /// <summary>Most players in a Purge.</summary>
    public const int PurgeMaxPlayers = 4;

    public required ulong Seed { get; init; }

    public MatchMode Mode { get; init; } = MatchMode.ProvingGround;

    public MapBounds Map { get; init; } = new(64, 64);

    public int Players { get; init; } = 2;

    public int UnitsPerPlayer { get; init; } = 100;

    /// <summary>Unit speed in cells per tick. Default 0.1 cells/tick = 1.5 cells per second.</summary>
    public Fix64 UnitSpeedPerTick { get; init; } = Fix64.FromRatio(1, 10);

    /// <summary>Ticks between each player's random orders. 0 disables them.</summary>
    public int OrderIntervalTicks { get; init; } = 15;

    public int OrdersPerInterval { get; init; } = 5;

    /// <summary>Skirmish only: who plays each side. Default: two scripted AIs.</summary>
    public IReadOnlyList<PlayerController> Controllers { get; init; } = [PlayerController.ScriptedAi, PlayerController.ScriptedAi];

    /// <summary>Skirmish only: each player's team. Players on one team share vision and win together.</summary>
    public IReadOnlyList<int> Teams { get; init; } = [0, 1];

    /// <summary>
    /// Lockstep input delay in ticks: a command issued on tick T runs on tick T + delay, and remote
    /// seats' turns before tick <see cref="InputDelay"/> are empty. 0 outside network matches.
    /// </summary>
    public int InputDelay { get; init; }

    /// <summary>Skirmish only: the procedural map to play on, or null for the hand-made duel/quad map.</summary>
    public MapSettings? GeneratedMap { get; init; }

    /// <summary>Swarm: how many creatures the Maw keeps alive (with nests: the most it may have alive). Purge: the most alive.</summary>
    public int Creatures { get; init; }

    /// <summary>
    /// Swarm: Spawning Pits on Cindrite fields around the fortress. 0 tops the swarm up from the map edges
    /// instead; with nests, the Maw breeds only from them and fields regrow. Purge: Spawning Pits on the map's
    /// fields away from the bases (fewer if the map has fewer such fields).
    /// </summary>
    public int Nests { get; init; }

    /// <summary>Purge only: how hard the Maw's director pushes (GDD section 3).</summary>
    public MawDifficulty Difficulty { get; init; } = MawDifficulty.Veteran;

    /// <summary>
    /// Purge only: the tier the nests start at (1 Spawning Pit, 2 Brood Nest, 3 Behemoth Den). Campaign missions start
    /// deeper-infested regions at higher tiers (GDD section 7).
    /// </summary>
    public int NestTier { get; init; } = 1;

    /// <summary>Purge only: credits every player starts with on top of the usual (campaign bonuses).</summary>
    public int BonusCredits { get; init; }

    /// <summary>Purge only: harvesters every player starts with beside the Construction Yard (campaign bonuses).</summary>
    public int BonusHarvesters { get; init; }

    /// <summary>Most extra starting harvesters per player.</summary>
    public const int MaxBonusHarvesters = 4;

    /// <summary>The default proving-ground match for <paramref name="seed"/>.</summary>
    public static MatchSpec ProvingGround(ulong seed) => new() { Seed = seed };

    /// <summary>A 1v1 Bulwark skirmish on the duel map, both sides played by the scripted M1 AI.</summary>
    public static MatchSpec Skirmish(ulong seed) => Skirmish(seed, 2);

    /// <summary>A free-for-all Bulwark skirmish for <paramref name="players"/> scripted AIs (2–4).</summary>
    public static MatchSpec Skirmish(ulong seed, int players)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, SkirmishMaps.MaxPlayers);
        var size = SkirmishMaps.SizeFor(players);
        return new MatchSpec
        {
            Seed = seed,
            Mode = MatchMode.Skirmish,
            Map = new MapBounds(size, size),
            Players = players,
            Controllers = Enumerable.Repeat(PlayerController.ScriptedAi, players).ToArray(),
            Teams = Enumerable.Range(0, players).ToArray(),
        };
    }

    /// <summary>
    /// The horde stress test: one Bulwark fortress with idle defenders in the middle of an open
    /// <see cref="Maps.SwarmFortress.Size"/>-cell map, and a Maw that keeps <paramref name="creatures"/> alive.
    /// </summary>
    public static MatchSpec Swarm(ulong seed, int creatures = SwarmDefaultCreatures, int nests = 0) => new()
    {
        Seed = seed,
        Mode = MatchMode.Swarm,
        Map = new MapBounds(SwarmFortress.Size, SwarmFortress.Size),
        Players = 1,
        Controllers = [PlayerController.IdleAi],
        Teams = [0],
        Creatures = creatures,
        Nests = nests,
    };

    /// <summary>
    /// A co-op Purge: <paramref name="players"/> Bulwark players (1–4, all scripted AIs until seats are changed with
    /// <see cref="WithController"/>) on one team, on a generated map, against <paramref name="nests"/> Spawning Pits.
    /// </summary>
    /// <param name="map">The generated map; null for the default Crossroads map with the match seed.</param>
    public static MatchSpec Purge(
        ulong seed,
        int players = 1,
        int nests = PurgeDefaultNests,
        int creatures = PurgeDefaultCreatures,
        MapSettings? map = null,
        MawDifficulty difficulty = MawDifficulty.Veteran)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, PurgeMaxPlayers);
        var settings = map ?? MapSettings.Default(MapArchetypes.Crossroads, (uint)(seed & uint.MaxValue));
        var size = MapGenerator.SizeOf(MapGenerator.ApplyLimits(settings, out _));
        return new MatchSpec
        {
            Seed = seed,
            Mode = MatchMode.Purge,
            Map = new MapBounds(size, size),
            Players = players,
            Controllers = Enumerable.Repeat(PlayerController.ScriptedAi, players).ToArray(),
            Teams = new int[players],
            GeneratedMap = settings,
            Creatures = creatures,
            Nests = nests,
            Difficulty = difficulty,
        };
    }

    /// <summary>The Maw's first wave in a Purge: bigger the more players there are (40 for one, 70 for four).</summary>
    public static int PurgeFirstWaveSize(int players) => 30 + (10 * players);

    /// <summary>How much each wave of a Purge outgrows the last (30 for one player, 60 for four).</summary>
    public static int PurgeWaveGrowth(int players) => 20 + (10 * players);

    public MatchSpec WithPlayers(int players) => this with { Players = players };

    /// <summary>Plays the skirmish on a generated map (its size comes from the settings).</summary>
    public MatchSpec WithGeneratedMap(MapSettings? settings)
    {
        if (settings is null)
        {
            var size = SkirmishMaps.SizeFor(Players);
            return this with { GeneratedMap = null, Map = new MapBounds(size, size) };
        }

        var generated = MapGenerator.SizeOf(MapGenerator.ApplyLimits(settings, out _));
        return this with { GeneratedMap = settings, Map = new MapBounds(generated, generated) };
    }

    /// <summary>Sets every player's team, in seat order.</summary>
    public MatchSpec WithTeams(params int[] teams) => this with { Teams = teams.ToArray() };

    /// <summary>Sets who plays <paramref name="player"/> in a skirmish.</summary>
    public MatchSpec WithController(int player, PlayerController controller)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, Controllers.Count);
        var controllers = Controllers.ToArray();
        controllers[player] = controller;
        return this with { Controllers = controllers };
    }

    /// <summary>The player seat controlled by a person, or -1 if every side is an AI.</summary>
    public int HumanPlayer
    {
        get
        {
            for (var player = 0; player < Controllers.Count; player++)
            {
                if (Controllers[player] == PlayerController.Human)
                {
                    return player;
                }
            }

            return -1;
        }
    }

    public MatchSpec WithUnitsPerPlayer(int units) => this with { UnitsPerPlayer = units };

    public MatchSpec WithMap(int width, int height) => this with { Map = new MapBounds(width, height) };

    public MatchSpec WithOrders(int intervalTicks, int ordersPerInterval) =>
        this with { OrderIntervalTicks = intervalTicks, OrdersPerInterval = ordersPerInterval };

    /// <summary>Throws if the spec cannot start a match.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Players, 1, nameof(Players));
        if (Mode == MatchMode.Skirmish)
        {
            if (Players < 2 || Players > SkirmishMaps.MaxPlayers)
            {
                throw new ArgumentOutOfRangeException(nameof(Players), Players, $"Skirmish maps have 2 to {SkirmishMaps.MaxPlayers} start positions.");
            }

            var size = GeneratedMap is { } generated ? MapGenerator.SizeOf(MapGenerator.ApplyLimits(generated, out _)) : SkirmishMaps.SizeFor(Players);
            if (Map.Width != size || Map.Height != size)
            {
                throw new ArgumentOutOfRangeException(nameof(Map), Map, "A skirmish's map size is set by its map (and, for hand-made maps, its player count).");
            }

            if (Controllers.Count != Players)
            {
                throw new ArgumentOutOfRangeException(nameof(Controllers), Controllers.Count, "A skirmish needs one controller per player.");
            }

            if (Teams.Count != Players)
            {
                throw new ArgumentOutOfRangeException(nameof(Teams), Teams.Count, "A skirmish needs one team per player.");
            }

            if (Teams.Distinct().Count() < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(Teams), "A skirmish needs at least two teams.");
            }
        }

        if (Mode == MatchMode.Purge)
        {
            if (Players > PurgeMaxPlayers)
            {
                throw new ArgumentOutOfRangeException(nameof(Players), Players, $"A Purge has 1 to {PurgeMaxPlayers} players.");
            }

            if (GeneratedMap is null)
            {
                throw new ArgumentOutOfRangeException(nameof(GeneratedMap), "A Purge is played on a generated map.");
            }

            var size = MapGenerator.SizeOf(MapGenerator.ApplyLimits(GeneratedMap, out _));
            if (Map.Width != size || Map.Height != size)
            {
                throw new ArgumentOutOfRangeException(nameof(Map), Map, "A Purge's map size is set by its map.");
            }

            if (Controllers.Count != Players || Teams.Count != Players)
            {
                throw new ArgumentOutOfRangeException(nameof(Controllers), Controllers.Count, "A Purge needs one controller and one team per player.");
            }

            if (Teams.Any(team => team != Teams[0]))
            {
                throw new ArgumentOutOfRangeException(nameof(Teams), "A Purge is co-op: every player is on one team.");
            }

            if (!Enum.IsDefined(Difficulty))
            {
                throw new ArgumentOutOfRangeException(nameof(Difficulty), Difficulty, "Unknown difficulty.");
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(Nests, 1, nameof(Nests));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Nests, PurgeMaxNests, nameof(Nests));
            ArgumentOutOfRangeException.ThrowIfLessThan(NestTier, 1, nameof(NestTier));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(NestTier, 3, nameof(NestTier));
            ArgumentOutOfRangeException.ThrowIfNegative(BonusCredits, nameof(BonusCredits));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(BonusCredits, 100_000, nameof(BonusCredits));
            ArgumentOutOfRangeException.ThrowIfNegative(BonusHarvesters, nameof(BonusHarvesters));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(BonusHarvesters, MaxBonusHarvesters, nameof(BonusHarvesters));
            ArgumentOutOfRangeException.ThrowIfLessThan(Creatures, 1, nameof(Creatures));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Creatures, SwarmMaxCreatures, nameof(Creatures));
        }

        if (Mode == MatchMode.Swarm)
        {
            if (Players != 1 || Controllers.Count != 1 || Teams.Count != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Players), Players, "The swarm test has one player, the fortress.");
            }

            if (Controllers[0] == PlayerController.ScriptedAi)
            {
                throw new ArgumentOutOfRangeException(nameof(Controllers), Controllers[0], "The fortress is held by a person (here or in a replay) or left idle.");
            }

            if (Map.Width != SwarmFortress.Size || Map.Height != SwarmFortress.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(Map), Map, $"The swarm test is played on a {SwarmFortress.Size}-cell square map.");
            }

            ArgumentOutOfRangeException.ThrowIfNegative(Creatures, nameof(Creatures));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Creatures, SwarmMaxCreatures, nameof(Creatures));
            ArgumentOutOfRangeException.ThrowIfNegative(Nests, nameof(Nests));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Nests, SwarmMaxNests, nameof(Nests));
        }

        if (Controllers.Count(controller => controller == PlayerController.Human) > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Controllers), "Only one person can play at this machine; network players are Remote seats.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Players, 8, nameof(Players));
        ArgumentOutOfRangeException.ThrowIfNegative(InputDelay, nameof(InputDelay));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(InputDelay, 30, nameof(InputDelay));
        ArgumentOutOfRangeException.ThrowIfNegative(UnitsPerPlayer, nameof(UnitsPerPlayer));
        ArgumentOutOfRangeException.ThrowIfNegative(OrderIntervalTicks, nameof(OrderIntervalTicks));
        ArgumentOutOfRangeException.ThrowIfNegative(OrdersPerInterval, nameof(OrdersPerInterval));
        if (Map.Width <= 0 || Map.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Map), Map, "Map must be at least one cell in each direction.");
        }

        if (UnitSpeedPerTick <= Fix64.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(UnitSpeedPerTick), UnitSpeedPerTick, "Units must move.");
        }
    }
}
