using System.Globalization;
using System.Text;
using Ashfall.Core;
using Ashfall.Headless;
using Ashfall.Session;
using Ashfall.Session.Replays;

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

if (options.ReplayIn is { } replayPath)
{
    var replay = Replay.Load(replayPath);
    Console.WriteLine(replay.ToString());
    var (hash, matches) = replay.Verify();
    Console.WriteLine(matches
        ? $"Replay reproduced the recorded final state {StateHasher.Format(hash)}."
        : $"DESYNC: replay ended at {StateHasher.Format(hash)}, recording ended at {StateHasher.Format(replay.FinalHash)}.");
    return matches ? 0 : 1;
}

if (options.IsNetwork)
{
    return NetworkRunner.Run(options, cancellation.Token);
}

var spec = options.ToMatchSpec();
Console.WriteLine(spec.Mode == MatchMode.Skirmish
    ? string.Create(
        CultureInfo.InvariantCulture,
        $"mode=skirmish seed={spec.Seed} players={spec.Players} teams={string.Join(',', spec.Teams)} map={spec.Map.Width}x{spec.Map.Height} tick-limit={options.Ticks} speed={options.Speed}")
    : string.Create(
        CultureInfo.InvariantCulture,
        $"mode=proving seed={spec.Seed} players={spec.Players} units/player={spec.UnitsPerPlayer} map={spec.Map.Width}x{spec.Map.Height} ticks={options.Ticks} speed={options.Speed}"));

MatchResult? first = null;
var allMatch = true;
for (var run = 1; run <= options.Repeat; run++)
{
    await using var match = await HeadlessMatch.StartAsync(spec, options.Speed).ConfigureAwait(false);
    var result = spec.Mode == MatchMode.Skirmish
        ? await RunSkirmishAsync(match, options, cancellation.Token).ConfigureAwait(false)
        : await match.RunTicksAsync(options.Ticks, cancellation.Token).ConfigureAwait(false);

    var outcome = spec.Mode != MatchMode.Skirmish ? string.Empty
        : result.IsOver ? string.Create(CultureInfo.InvariantCulture, $" winner=team {result.WinningTeam} ({result.FinalTick / 900.0:F1} game minutes)")
        : " winner=none (tick limit reached)";
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"run {run}: tick={result.FinalTick} hash={result.FinalStateHashText} elapsed={result.WallClockElapsed.TotalMilliseconds:F0}ms rate={result.TicksPerSecond} ticks/s{outcome}"));

    if (first is null)
    {
        first = result;
        if (options.RecordOut is not null)
        {
            Replay.From(match).Save(options.RecordOut);
        }
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

if (!allMatch)
{
    return 1;
}

return spec.Mode == MatchMode.Skirmish && first is { IsOver: false } ? 3 : 0;

// Runs to completion, pausing every --report-every ticks to print a status report.
static async Task<MatchResult> RunSkirmishAsync(HeadlessMatch match, HeadlessOptions options, CancellationToken cancellationToken)
{
    if (options.ReportEvery <= 0)
    {
        return await match.RunToCompletionAsync(options.Ticks, cancellationToken).ConfigureAwait(false);
    }

    MatchResult result;
    do
    {
        var remaining = options.Ticks - match.Tick;
        result = await match.RunToCompletionAsync(Math.Min(options.ReportEvery, remaining), cancellationToken).ConfigureAwait(false);
        Console.WriteLine(MatchReport.Describe(match.Simulation));
    }
    while (!result.IsOver && match.Tick < options.Ticks);

    return result;
}
