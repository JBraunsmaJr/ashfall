using System.Globalization;
using Ashfall.Session;

namespace Ashfall.Client;

/// <summary>Command-line options for the <c>ashfall</c> client.</summary>
internal sealed record ClientOptions
{
    public const string Usage = """
        ashfall — the Ashfall client.

        Usage: ashfall [options]

          --seed <n>        Proving-ground seed (default 20261017)
          --speed <speed>   Game speed: 1x (default), 4x, 0.5x, unthrottled
          --frames <n>      Render n frames, then exit (smoke tests and CI)
          --validation      Enable the Vulkan validation layer (default in Debug builds)
          --no-validation   Disable it
          --no-vsync        Present as fast as possible (mailbox or immediate)
          --size <WxH>      Window size in pixels (default 1280x720)
          -h, --help        Show this help
        """;

    public ulong Seed { get; init; } = ProvingGround.DefaultSeed;

    public GameSpeed Speed { get; init; } = GameSpeed.RealTime;

    /// <summary>Frames to render before exiting; 0 runs until the window closes.</summary>
    public long Frames { get; init; }

    public bool Validation { get; init; }

    public bool VSync { get; init; } = true;

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    public bool ShowHelp { get; init; }

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
            }

            if (i + 1 >= args.Count)
            {
                error = $"Missing value for {name}.";
                return null;
            }

            var value = args[++i];
            switch (name)
            {
                case "--seed" when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed):
                    options = options with { Seed = seed };
                    break;
                case "--speed" when GameSpeed.TryParse(value, out var speed):
                    options = options with { Speed = speed };
                    break;
                case "--frames" when long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var frames):
                    options = options with { Frames = frames };
                    break;
                case "--size" when TryParseSize(value, out var width, out var height):
                    options = options with { Width = width, Height = height };
                    break;
                default:
                    error = $"Invalid option or value: {name} {value}";
                    return null;
            }
        }

        return options;
    }

    private static bool TryParseSize(string text, out int width, out int height)
    {
        width = height = 0;
        var parts = text.Split('x', 'X');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
            && width >= 64 && height >= 64;
    }
}
