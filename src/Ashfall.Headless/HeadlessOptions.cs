using System.Globalization;
using Ashfall.Session;

namespace Ashfall.Headless;

/// <summary>Command-line options for <c>ashfall-headless</c>.</summary>
internal sealed record HeadlessOptions
{
    public const string Usage = """
        ashfall-headless — run Ashfall matches without a window.

        Usage: ashfall-headless [options]

          --seed <n>              Match seed (default 20261017)
          --ticks <n>             Ticks to run (default 1800 = 2 minutes of game time)
          --players <n>           Players, 1-8 (default 2)
          --units <n>             Units per player (default 100)
          --map <n>               Square map size in cells (default 64)
          --speed <speed>         unthrottled | 1x | 4x | 0.5x | 1/2x (default unthrottled)
          --repeat <n>            Run the match n times and fail if any final hash differs (default 1)
          --hash-out <file>       Write the final state hash to a file (for CI comparisons)
          --checkpoints-out <f>   Write every 30-tick hash checkpoint to a file
          -h, --help              Show this help

        Exit codes: 0 success, 1 hashes differed between repeats, 2 invalid arguments.
        """;

    public ulong Seed { get; init; } = ProvingGround.DefaultSeed;

    public long Ticks { get; init; } = ProvingGround.DefaultTicks;

    public int Players { get; init; } = 2;

    public int Units { get; init; } = 100;

    public int MapSize { get; init; } = 64;

    public GameSpeed Speed { get; init; } = GameSpeed.Unthrottled;

    public int Repeat { get; init; } = 1;

    public string? HashOut { get; init; }

    public string? CheckpointsOut { get; init; }

    public bool ShowHelp { get; init; }

    public MatchSpec ToMatchSpec() =>
        MatchSpec.ProvingGround(Seed)
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

            if (i + 1 >= args.Count)
            {
                error = $"Missing value for {name}.";
                return null;
            }

            var value = args[++i];
            var parsed = name switch
            {
                "--seed" => TryUInt64(value, v => options = options with { Seed = v }),
                "--ticks" => TryInt64(value, 0, v => options = options with { Ticks = v }),
                "--players" => TryInt32(value, 1, v => options = options with { Players = v }),
                "--units" => TryInt32(value, 0, v => options = options with { Units = v }),
                "--map" => TryInt32(value, 1, v => options = options with { MapSize = v }),
                "--repeat" => TryInt32(value, 1, v => options = options with { Repeat = v }),
                "--speed" => TrySpeed(value, v => options = options with { Speed = v }),
                "--hash-out" => Set(() => options = options with { HashOut = value }),
                "--checkpoints-out" => Set(() => options = options with { CheckpointsOut = value }),
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
