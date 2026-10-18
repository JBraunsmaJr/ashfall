using System.Globalization;
using Ashfall.Session;

namespace Ashfall.Headless;

/// <summary>Command-line options for <c>ashfall-headless</c>.</summary>
internal sealed record HeadlessOptions
{
    public const string Usage = """
        ashfall-headless — run Ashfall matches without a window.

        Usage: ashfall-headless [options]

          --mode <mode>           proving (default): wandering units, fixed tick count
                                  skirmish: Bulwark skirmish between scripted AIs, run until a team wins
          --seed <n>              Match seed (default 20261017)
          --ticks <n>             Proving: ticks to run (default 1800 = 2 minutes of game time)
                                  Skirmish: tick limit (default 54000 = 60 minutes); exit code 3 if no winner
          --players <n>           Proving: players, 1-8 (default 2). Skirmish: 2-4 (3-4 play on the quad map)
          --teams <list>          Skirmish: team per player, e.g. 0,1,0,1 (default: free-for-all)
          --units <n>             Proving only: units per player (default 100)
          --map <n>               Proving only: square map size in cells (default 64)
          --speed <speed>         unthrottled | 1x | 4x | 0.5x | 1/2x (default unthrottled)
          --report-every <n>      Print a status report every n ticks (skirmish tuning)
          --repeat <n>            Run the match n times and fail if any final hash differs (default 1)
          --hash-out <file>       Write the final state hash to a file (for CI comparisons)
          --checkpoints-out <f>   Write every 30-tick hash checkpoint to a file
          --record <file>         Skirmish: save a replay of the (first) run
          --replay <file>         Play a replay and check it reaches its recorded final hash
          -h, --help              Show this help

        Network play (scripted-AI bots over UDP; every bot runs the full lockstep protocol):
          --host                  Host a lobby and start once every slot is filled
          --humans <n>            With --host: network players including the host (default 2)
          --ais <n>               With --host: AI players run on every peer (default 0)
          --join <ip:port>        Join the lobby at ip:port
          --rejoin <ip:port>      Rejoin a match in progress through any player in it
          --port <n>              UDP port to listen on (default 47600 when hosting, any otherwise)
          --name <text>           Display name (default BOT)
          --identity <file>       Keep the anonymous id here (needed to rejoin as the same player)
          --input-delay <n>       With --host: lockstep delay in ticks (default: measured, 2-6)
          --lan <n>               Run n bots in this process over loopback UDP (a whole LAN match)
          --timeout <seconds>     Give up on a network match after this long (default 600)

        Exit codes: 0 success, 1 hashes differed between repeats (or a replay mismatched),
                    2 invalid arguments, 3 skirmish reached the tick limit without a winner,
                    4 peers desynced, 5 the network match failed or timed out.
        """;

    /// <summary>Skirmish tick limit when --ticks is not given: 60 minutes of game time.</summary>
    public const long DefaultSkirmishTickLimit = MatchSpec.SkirmishTickLimit;

    public MatchMode Mode { get; init; } = MatchMode.ProvingGround;

    public ulong Seed { get; init; } = ProvingGround.DefaultSeed;

    /// <summary>Explicit --ticks value, if given.</summary>
    public long? TicksOption { get; init; }

    public long Ticks => TicksOption ?? (Mode == MatchMode.Skirmish ? DefaultSkirmishTickLimit : ProvingGround.DefaultTicks);

    public int Players { get; init; } = 2;

    public IReadOnlyList<int>? Teams { get; init; }

    public int Units { get; init; } = 100;

    public int MapSize { get; init; } = 64;

    public GameSpeed Speed { get; init; } = GameSpeed.Unthrottled;

    public int Repeat { get; init; } = 1;

    /// <summary>Ticks between status reports; 0 for none.</summary>
    public long ReportEvery { get; init; }

    public string? HashOut { get; init; }

    public string? CheckpointsOut { get; init; }

    public bool ShowHelp { get; init; }

    public string? RecordOut { get; init; }

    public string? ReplayIn { get; init; }

    public bool Host { get; init; }

    public string? Join { get; init; }

    public string? Rejoin { get; init; }

    public int Humans { get; init; } = 2;

    public int Ais { get; init; }

    public int? Port { get; init; }

    public string Name { get; init; } = "BOT";

    public string? Identity { get; init; }

    public int? InputDelay { get; init; }

    public int Lan { get; init; }

    public int TimeoutSeconds { get; init; } = 600;

    public bool IsNetwork => Host || Join is not null || Rejoin is not null || Lan > 0;

    public MatchSpec ToMatchSpec() =>
        Mode == MatchMode.Skirmish
            ? Teams is null ? MatchSpec.Skirmish(Seed, Players) : MatchSpec.Skirmish(Seed, Players).WithTeams([.. Teams])
            : MatchSpec.ProvingGround(Seed)
                .WithPlayers(Players)
                .WithUnitsPerPlayer(Units)
                .WithMap(MapSize, MapSize);

    /// <summary>Parses arguments; returns null and sets <paramref name="error"/> when they are invalid.</summary>
    public static HeadlessOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        var options = new HeadlessOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            if (name is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            if (name == "--host")
            {
                options = options with { Host = true, Mode = MatchMode.Skirmish };
                continue;
            }

            if (i + 1 >= args.Count)
            {
                error = $"Missing value for {name}.";
                return null;
            }

            var value = args[++i];
            var parsed = name switch
            {
                "--seed" => TryUInt64(value, v => options = options with { Seed = v }),
                "--ticks" => TryInt64(value, 0, v => options = options with { TicksOption = v }),
                "--mode" => TryMode(value, v => options = options with { Mode = v }),
                "--players" => TryInt32(value, 1, v => options = options with { Players = v }),
                "--teams" => TryTeams(value, v => options = options with { Teams = v }),
                "--units" => TryInt32(value, 0, v => options = options with { Units = v }),
                "--map" => TryInt32(value, 1, v => options = options with { MapSize = v }),
                "--report-every" => TryInt64(value, 0, v => options = options with { ReportEvery = v }),
                "--repeat" => TryInt32(value, 1, v => options = options with { Repeat = v }),
                "--speed" => TrySpeed(value, v => options = options with { Speed = v }),
                "--hash-out" => Set(() => options = options with { HashOut = value }),
                "--checkpoints-out" => Set(() => options = options with { CheckpointsOut = value }),
                "--record" => Set(() => options = options with { RecordOut = value }),
                "--replay" => Set(() => options = options with { ReplayIn = value }),
                "--humans" => TryInt32(value, 1, v => options = options with { Humans = v }),
                "--ais" => TryInt32(value, 0, v => options = options with { Ais = v }),
                "--join" => Set(() => options = options with { Join = value, Mode = MatchMode.Skirmish }),
                "--rejoin" => Set(() => options = options with { Rejoin = value, Mode = MatchMode.Skirmish }),
                "--port" => TryInt32(value, 0, v => options = options with { Port = v }),
                "--name" => Set(() => options = options with { Name = value }),
                "--identity" => Set(() => options = options with { Identity = value }),
                "--input-delay" => TryInt32(value, 0, v => options = options with { InputDelay = v }),
                "--lan" => TryInt32(value, 2, v => options = options with { Lan = v, Mode = MatchMode.Skirmish }),
                "--timeout" => TryInt32(value, 1, v => options = options with { TimeoutSeconds = v }),
                _ => false,
            };

            if (!parsed)
            {
                error = $"Invalid option or value: {name} {value}";
                return null;
            }
        }

        return options;
    }

    private static bool TryTeams(string text, Action<IReadOnlyList<int>> apply)
    {
        var teams = new List<int>();
        foreach (var part in text.Split(','))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var team) || team > 7)
            {
                return false;
            }

            teams.Add(team);
        }

        apply(teams);
        return true;
    }

    private static bool Set(Action apply)
    {
        apply();
        return true;
    }

    private static bool TryUInt64(string text, Action<ulong> apply)
    {
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        apply(value);
        return true;
    }

    private static bool TryInt64(string text, long min, Action<long> apply)
    {
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min)
        {
            return false;
        }

        apply(value);
        return true;
    }

    private static bool TryInt32(string text, int min, Action<int> apply)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min)
        {
            return false;
        }

        apply(value);
        return true;
    }

    private static bool TryMode(string text, Action<MatchMode> apply)
    {
        switch (text.ToUpperInvariant())
        {
            case "PROVING":
                apply(MatchMode.ProvingGround);
                return true;
            case "SKIRMISH":
                apply(MatchMode.Skirmish);
                return true;
            default:
                return false;
        }
    }

    private static bool TrySpeed(string text, Action<GameSpeed> apply)
    {
        if (!GameSpeed.TryParse(text, out var speed))
        {
            return false;
        }

        apply(speed);
        return true;
    }
}
