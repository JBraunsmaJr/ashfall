using System.Globalization;
using System.Text;
using Ashfall.Core;
using Ashfall.Headless;
using Ashfall.Session;

var options = HeadlessOptions.Parse(args, out var error);
if (options is null)
{
    await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
    await Console.Error.WriteLineAsync(HeadlessOptions.Usage).ConfigureAwait(false);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(HeadlessOptions.Usage);
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var spec = options.ToMatchSpec();
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"seed={spec.Seed} players={spec.Players} units/player={spec.UnitsPerPlayer} map={spec.Map.Width}x{spec.Map.Height} ticks={options.Ticks} speed={options.Speed}"));

MatchResult? first = null;
var allMatch = true;
for (var run = 1; run <= options.Repeat; run++)
{
    await using var match = await HeadlessMatch.StartAsync(spec, options.Speed).ConfigureAwait(false);
    var result = await match.RunTicksAsync(options.Ticks, cancellation.Token).ConfigureAwait(false);

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"run {run}: tick={result.FinalTick} hash={result.FinalStateHashText} elapsed={result.WallClockElapsed.TotalMilliseconds:F0}ms rate={result.TicksPerSecond} ticks/s"));

    if (first is null)
    {
        first = result;
    }
    else if (result.FinalStateHash != first.FinalStateHash || !result.Checkpoints.SequenceEqual(first.Checkpoints))
    {
        allMatch = false;
        await Console.Error.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"DESYNC: run {run} ended with {result.FinalStateHashText}, run 1 ended with {first.FinalStateHashText}")).ConfigureAwait(false);
    }
}

if (first is not null)
{
    if (options.HashOut is not null)
    {
        await File.WriteAllTextAsync(options.HashOut, first.FinalStateHashText + "\n").ConfigureAwait(false);
    }

    if (options.CheckpointsOut is not null)
    {
        var lines = new StringBuilder();
        foreach (var checkpoint in first.Checkpoints)
        {
            lines.Append(CultureInfo.InvariantCulture, $"{checkpoint.Tick} {StateHasher.Format(checkpoint.StateHash)}\n");
        }

        await File.WriteAllTextAsync(options.CheckpointsOut, lines.ToString()).ConfigureAwait(false);
    }
}

if (options.Repeat > 1)
{
    Console.WriteLine(allMatch ? $"All {options.Repeat} runs produced identical hashes." : "Runs diverged.");
}

return allMatch ? 0 : 1;
