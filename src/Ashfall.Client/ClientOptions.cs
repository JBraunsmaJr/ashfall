using System.Globalization;
using Ashfall.MapGen;
using Ashfall.Session;
using Ashfall.Simulation.Horde;

namespace Ashfall.Client;

/// <summary>Command-line options for the <c>ashfall</c> client.</summary>
internal sealed record ClientOptions
{
    public const string Usage = """
        ashfall — the Ashfall client.

        Usage: ashfall [options]

        With no options the main menu opens: the campaign, a co-op Purge, a skirmish, host a game or join one.
        Options that only make sense for a skirmish (--seed, --opponent, --frames, --screenshot, ...)
        start one directly; --menu shows the menu anyway.

          --menu                 Open the main menu even with other options given
          --skirmish             Start a skirmish directly, skipping the menu
          --seed <n>             Match seed (default 20261017)
          --opponent <ai>        scripted (default): the M1 AI builds, trains and attacks
                                 idle: a dummy opponent that never acts (learn the controls)
          --spectate             Watch two scripted AIs play (no local player, no fog)
          --autopilot            The scripted AI plays your side too (demos, smoke tests)
          --speed <speed>        Game speed: 1x (default), 2x, 4x, 0.5x, unthrottled
          --frames <n>           Render n frames, then exit (smoke tests and CI)
          --until-tick <n>       Exit once the simulation reaches tick n
          --screenshot <file>    Save the last frame as a PNG before exiting
          --look-at <x,y>        Start with the camera over cell (x, y) instead of your base
          --zoom <step>          Start at zoom step 0 (far), 1 (default) or 2 (near)
          --validation           Enable the Vulkan validation layer (default in Debug builds)
          --no-validation        Disable it
          --no-vsync             Present as fast as possible (mailbox or immediate)
          --size <WxH>           Window size in pixels (default 1280x720)
          --archetype <id>       Play a generated map: crossroads or riverlands (skirmish or --host)
          --map-code <code>      Play the generated map with this code (e.g. CRS-...)
          --slider <name=value>  Generated map slider 0-100 (size, richness, elevation, water,
                                 chokepoints, basespacing, vegetation); repeatable
          --record <file>        Save a replay of the match when it ends or you leave
          --purge                Co-op against the Maw: destroy every nest on a generated map (crossroads
                                 by default; --archetype, --map-code and --slider pick another)
          --allies <n>           With --purge: scripted-AI allies on your team (0-3, default 1)
          --difficulty <tier>    With --purge: recruit, veteran, elite or extinction (default veteran)
          --swarm                Hold the line: command the Bulwark fortress against the Maw
          --nests <n>            With --swarm: the Maw breeds from n Spawning Pits (1-8, default 4) that feed
                                 and evolve; 0 sends a horde from the map edges instead
                                 With --purge: Spawning Pits on the map's fields (1-12, default 4)
          --creatures <n>        With --swarm: the most creatures alive at once (default 2000)
                                 With --purge: a cap on the waves' growth (default 1500)
          --campaign             Open the campaign screen: continue the saved campaign, or start one
          --campaign-code <code> Start this campaign (e.g. ASH-0G000-0041E), replacing the save
          --campaign-save <file> Keep the campaign here (default: next to the menu settings)
          --campaign-missions <n> With --autopilot: play n missions (the autopilot picks them), then show the map
                                 (--allies sets the AI allies in every mission, default 1)
          -h, --help             Show this help

        Multiplayer (peer to peer; the host forwards one UDP port, default 47600):
          --host                 Host a lobby; --humans and --ais set the slots
          --humans <n>           Network players including you (default 2)
          --ais <n>              AI players (default 0); 2-4 players in total
          --join <ip:port>       Join the lobby at ip:port
          --rejoin <ip:port>     Rejoin a match in progress (after a crash) through any player in it
          --port <n>             UDP port to listen on (default 47600 when hosting, any otherwise)
          --name <text>          Your display name (default PLAYER)

        In game: left-click selects (drag for a box), right-click moves or attacks, A then click
        attack-moves, S stops, Ctrl+0-9 stores a control group and 0-9 recalls it (twice to jump),
        arrows / screen edges / middle-drag scroll, mouse wheel zooms, H centres on your base,
        +/- change speed, P pauses, Esc cancels; Esc twice with nothing to cancel leaves the match.
        """;

    public ulong Seed { get; init; } = ProvingGround.DefaultSeed;

    public PlayerController Opponent { get; init; } = PlayerController.ScriptedAi;

    public bool Spectate { get; init; }

    /// <summary>Play the swarm fortress against the Maw instead of a skirmish.</summary>
    public bool Swarm { get; init; }

    /// <summary>Play a co-op Purge against the Maw instead of a skirmish.</summary>
    public bool Purge { get; init; }

    /// <summary>Purge: scripted-AI allies on the local player's team.</summary>
    public int Allies { get; init; } = 1;

    /// <summary>Purge: how hard the Maw's director pushes.</summary>
    public MawDifficulty Difficulty { get; init; } = MawDifficulty.Veteran;

    /// <summary>Explicit --nests, if given (swarm default 4, Purge default <see cref="MatchSpec.PurgeDefaultNests"/>).</summary>
    public int? Nests { get; init; }

    /// <summary>Explicit --creatures, if given (swarm default 2000, Purge default <see cref="MatchSpec.PurgeDefaultCreatures"/>).</summary>
    public int? Creatures { get; init; }

    public bool Autopilot { get; init; }

    public GameSpeed Speed { get; init; } = GameSpeed.RealTime;

    /// <summary>Frames to render before exiting; 0 runs until the window closes.</summary>
    public long Frames { get; init; }

    /// <summary>Simulation tick at which to exit; 0 means no limit.</summary>
    public long UntilTick { get; init; }

    public string? Screenshot { get; init; }

    public (float X, float Y)? LookAt { get; init; }

    public int Zoom { get; init; } = 1;

    public bool Validation { get; init; }

    public bool VSync { get; init; } = true;

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    public bool ShowHelp { get; init; }

    public string? Record { get; init; }

    public bool Host { get; init; }

    public int Humans { get; init; } = 2;

    public int Ais { get; init; }

    public string? Join { get; init; }

    public string? Rejoin { get; init; }

    public int? Port { get; init; }

    public string Name { get; init; } = "PLAYER";

    /// <summary>Open the campaign screen rather than the menu or a match.</summary>
    public bool Campaign { get; init; }

    /// <summary>A new campaign to start, replacing the save.</summary>
    public string? CampaignCode { get; init; }

    /// <summary>Where the campaign is saved; null for the default.</summary>
    public string? CampaignSave { get; init; }

    /// <summary>With --autopilot: missions to play before handing the campaign screen back.</summary>
    public int CampaignMissions { get; init; }

    public bool IsNetwork => Host || Join is not null || Rejoin is not null;

    /// <summary>The generated map to play (from --archetype/--slider or --map-code), or null for the hand-made one.</summary>
    public MapSettings? GeneratedMap { get; init; }

    public bool Menu { get; init; }

    public bool Skirmish { get; init; }

    /// <summary>--name was given (it overrides the name the menu remembered).</summary>
    public bool NameGiven { get; init; }

    /// <summary>--seed was given (otherwise a hosted game picks a random seed).</summary>
    public bool SeedGiven { get; init; }

    /// <summary>The generated map came from --archetype, so its seed follows the match seed.</summary>
    private bool MapSeedFromMatch { get; init; }

    /// <summary>Set by options that describe a skirmish, so it starts without the menu.</summary>
    private bool SkirmishImplied { get; init; }

    /// <summary>Whether the client opens on the main menu rather than going straight into a mode.</summary>
    public bool StartsInMenu => Menu || (!IsNetwork && !Campaign && !Skirmish && !SkirmishImplied);

    public MatchSpec ToSpec()
    {
        if (Purge)
        {
            var map = GeneratedMap ?? MapSettings.Default(MapArchetypes.Crossroads, (uint)(Seed & uint.MaxValue));
            var purge = MatchSpec.Purge(
                Seed, 1 + Allies, Nests ?? MatchSpec.PurgeDefaultNests, Creatures ?? MatchSpec.PurgeDefaultCreatures, map, Difficulty);
            return Spectate ? purge : purge.WithController(0, PlayerController.Human);
        }

        if (Swarm)
        {
            var swarm = MatchSpec.Swarm(Seed, Creatures ?? 2_000, Nests ?? 4);
            return Spectate ? swarm : swarm.WithController(0, PlayerController.Human);
        }

        var spec = MatchSpec.Skirmish(Seed).WithGeneratedMap(GeneratedMap);
        return Spectate
            ? spec
            : spec.WithController(0, PlayerController.Human).WithController(1, Opponent);
    }

    public static ClientOptions? Parse(IReadOnlyList<string> args, bool validationByDefault, out string? error)
    {
        error = null;
        var options = new ClientOptions { Validation = validationByDefault };
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            switch (name)
            {
                case "-h" or "--help":
                    return options with { ShowHelp = true };
                case "--validation":
                    options = options with { Validation = true };
                    continue;
                case "--no-validation":
                    options = options with { Validation = false };
                    continue;
                case "--no-vsync":
                    options = options with { VSync = false };
                    continue;
                case "--spectate":
                    options = options with { Spectate = true, SkirmishImplied = true };
                    continue;
                case "--autopilot":
                    options = options with { Autopilot = true, SkirmishImplied = true };
                    continue;
                case "--menu":
                    options = options with { Menu = true };
                    continue;
                case "--skirmish":
                    options = options with { Skirmish = true };
                    continue;
                case "--swarm":
                    options = options with { Swarm = true, Purge = false, SkirmishImplied = true };
                    continue;
                case "--purge":
                    options = options with { Purge = true, Swarm = false, SkirmishImplied = true };
                    continue;
                case "--host":
                    options = options with { Host = true };
                    continue;
                case "--campaign":
                    options = options with { Campaign = true };
                    continue;
            }

            if (i + 1 >= args.Count)
            {
                error = $"Missing value for {name}.";
                return null;
            }

            var value = args[++i];
            switch (name)
            {
                case "--nests" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var nests) && nests <= MatchSpec.PurgeMaxNests:
                    options = options with { Nests = nests, Swarm = !options.Purge, SkirmishImplied = true };
                    break;
                case "--creatures" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var creatures) && creatures is >= 1 and <= MatchSpec.SwarmMaxCreatures:
                    options = options with { Creatures = creatures, Swarm = !options.Purge, SkirmishImplied = true };
                    break;
                case "--difficulty" when Enum.TryParse<MawDifficulty>(value, ignoreCase: true, out var difficulty) && Enum.IsDefined(difficulty) && !int.TryParse(value, out _):
                    options = options with { Difficulty = difficulty, Purge = true, Swarm = false, SkirmishImplied = true };
                    break;
                case "--allies" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var allies) && allies < MatchSpec.PurgeMaxPlayers:
                    options = options with { Allies = allies, Purge = true, Swarm = false, SkirmishImplied = true };
                    break;
                case "--seed" when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed):
                    options = options with { Seed = seed, SeedGiven = true, SkirmishImplied = true };
                    break;
                case "--opponent" when value is "scripted" or "idle":
                    options = options with { Opponent = value == "idle" ? PlayerController.IdleAi : PlayerController.ScriptedAi, SkirmishImplied = true };
                    break;
                case "--speed" when GameSpeed.TryParse(value, out var speed):
                    options = options with { Speed = speed };
                    break;
                case "--frames" when long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var frames):
                    options = options with { Frames = frames, SkirmishImplied = true };
                    break;
                case "--until-tick" when long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var tick):
                    options = options with { UntilTick = tick, SkirmishImplied = true };
                    break;
                case "--screenshot" when value.Length > 0:
                    options = options with { Screenshot = value, SkirmishImplied = true };
                    break;
                case "--look-at" when TryParsePoint(value, out var point):
                    options = options with { LookAt = point, SkirmishImplied = true };
                    break;
                case "--campaign-code" when Ashfall.Campaign.CampaignCode.TryParse(value, out _):
                    options = options with { CampaignCode = value, Campaign = true };
                    break;
                case "--campaign-save" when value.Length > 0:
                    options = options with { CampaignSave = value, Campaign = true };
                    break;
                case "--campaign-missions" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var missions) && missions >= 1:
                    options = options with { CampaignMissions = missions, Campaign = true };
                    break;
                case "--record" when value.Length > 0:
                    options = options with { Record = value };
                    break;
                case "--humans" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var humans) && humans is >= 1 and <= 4:
                    options = options with { Humans = humans };
                    break;
                case "--ais" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ais) && ais is >= 0 and <= 3:
                    options = options with { Ais = ais };
                    break;
                case "--join" when value.Contains(':', StringComparison.Ordinal):
                    options = options with { Join = value };
                    break;
                case "--rejoin" when value.Contains(':', StringComparison.Ordinal):
                    options = options with { Rejoin = value };
                    break;
                case "--port" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port <= 65535:
                    options = options with { Port = port };
                    break;
                case "--archetype" when MapArchetypes.Find(value) is { } archetype:
                    options = options with { GeneratedMap = new MapSettings(archetype, 0, options.GeneratedMap?.Sliders), MapSeedFromMatch = true, SkirmishImplied = true };
                    break;
                case "--map-code" when MapSettings.TryParseCode(value, out var code):
                    options = options with { GeneratedMap = code, MapSeedFromMatch = false, SkirmishImplied = true };
                    break;
                case "--slider" when options.GeneratedMap is { } map && TryParseSlider(value, out var slider, out var level):
                    options = options with { GeneratedMap = map.With(slider, level) };
                    break;
                case "--name" when value.Length > 0:
                    options = options with { Name = value, NameGiven = true };
                    break;
                case "--zoom" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var zoom) && zoom <= 2:
                    options = options with { Zoom = zoom };
                    break;
                case "--size" when TryParseSize(value, out var width, out var height):
                    options = options with { Width = width, Height = height };
                    break;
                default:
                    error = $"Invalid option or value: {name} {value}";
                    return null;
            }
        }

        if (options.Swarm && options.Nests > MatchSpec.SwarmMaxNests)
        {
            error = $"Invalid option or value: --nests {options.Nests} (the swarm fortress has room for {MatchSpec.SwarmMaxNests})";
            return null;
        }

        if (options.MapSeedFromMatch && options.GeneratedMap is { } generated)
        {
            options = options with { GeneratedMap = generated.WithSeed((uint)(options.Seed & uint.MaxValue)) };
        }

        return options;
    }

    private static bool TryParseSlider(string text, out MapSlider slider, out int value)
    {
        slider = default;
        value = 0;
        var parts = text.Split('=');
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out value) || value > 100)
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<MapSlider>())
        {
            if (string.Equals(candidate.ToString(), parts[0], StringComparison.OrdinalIgnoreCase))
            {
                slider = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryParsePoint(string text, out (float X, float Y) point)
    {
        point = default;
        var parts = text.Split(',');
        if (parts.Length != 2
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        point = (x, y);
        return true;
    }

    private static bool TryParseSize(string text, out int width, out int height)
    {
        width = height = 0;
        var parts = text.Split('x', 'X');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
            && width >= 640 && height >= 480;
    }
}
