using System.Globalization;
using Ashfall.Core;
using Ashfall.Session;

namespace Ashfall.Determinism.Tests;

/// <summary>
/// The determinism gates: each reference match must reach the same state hash, at every 30-tick
/// checkpoint, on every OS, CPU and build configuration. CI runs this on Windows and Linux.
/// <list type="bullet">
/// <item><b>proving-ground</b> (M0): 200 wandering units on an open map for two minutes.</item>
/// <item><b>skirmish</b> (M1): the default 1v1 Bulwark duel between scripted AIs, played to victory.</item>
/// </list>
/// </summary>
/// <remarks>
/// If a change is meant to alter simulation behaviour, regenerate the golden files with:
/// <code>
/// dotnet run --project src/Ashfall.Headless -c Release -- --hash-out tests/Ashfall.Determinism.Tests/Golden/proving-ground.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/proving-ground.checkpoints
/// dotnet run --project src/Ashfall.Headless -c Release -- --mode skirmish --hash-out tests/Ashfall.Determinism.Tests/Golden/skirmish.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/skirmish.checkpoints
/// </code>
/// and say why in the commit message. Never regenerate them to make an unexplained failure go away.
/// </remarks>
public class GoldenReplayTests
{
    public static TheoryData<string> Replays => new() { "proving-ground", "skirmish" };

    [Theory]
    [MemberData(nameof(Replays))]
    public async Task Final_state_hash_matches_the_golden_file(string replay)
    {
        var result = await RunAsync(replay);
        var golden = (await File.ReadAllTextAsync(GoldenPath(replay + ".hash"), TestContext.Current.CancellationToken)).Trim();

        Assert.Equal(golden, result.FinalStateHashText);
    }

    [Theory]
    [MemberData(nameof(Replays))]
    public async Task Every_checkpoint_matches_the_golden_file(string replay)
    {
        var result = await RunAsync(replay);
        var golden = await File.ReadAllLinesAsync(GoldenPath(replay + ".checkpoints"), TestContext.Current.CancellationToken);
        var expected = golden
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(' '))
            .Select(parts => new HashCheckpoint(
                long.Parse(parts[0], CultureInfo.InvariantCulture),
                ulong.Parse(parts[1].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)))
            .ToList();

        // Report the first divergent tick before comparing lengths: that is where a desync investigation starts.
        var shared = Math.Min(expected.Count, result.Checkpoints.Count);
        var first = Enumerable.Range(0, shared).FirstOrDefault(i => expected[i] != result.Checkpoints[i], -1);
        if (first >= 0)
        {
            Assert.Fail(
                $"First divergence at tick {expected[first].Tick}: expected {StateHasher.Format(expected[first].StateHash)}, " +
                $"got {StateHasher.Format(result.Checkpoints[first].StateHash)}.");
        }

        Assert.Equal(expected.Count, result.Checkpoints.Count);
    }

    private static async Task<MatchResult> RunAsync(string replay)
    {
        var cancellation = TestContext.Current.CancellationToken;
        if (replay == "skirmish")
        {
            await using var skirmish = await HeadlessMatch.StartAsync(MatchSpec.Skirmish(ProvingGround.DefaultSeed), GameSpeed.Unthrottled);
            return await skirmish.RunToCompletionAsync(MatchSpec.SkirmishTickLimit, cancellation);
        }

        await using var match = await HeadlessMatch.StartAsync(MatchSpec.ProvingGround(ProvingGround.DefaultSeed), GameSpeed.Unthrottled);
        return await match.RunTicksAsync(ProvingGround.DefaultTicks, cancellation);
    }

    private static string GoldenPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Golden", fileName);
}
