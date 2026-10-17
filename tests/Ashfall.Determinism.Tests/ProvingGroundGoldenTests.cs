using System.Globalization;
using Ashfall.Core;
using Ashfall.Session;

namespace Ashfall.Determinism.Tests;

/// <summary>
/// The M0 exit gate: the proving-ground run must reach the same state hash, at every 30-tick
/// checkpoint, on every OS, CPU and build configuration. CI runs this on Windows and Linux.
/// </summary>
/// <remarks>
/// If a change is meant to alter simulation behaviour, regenerate the golden files with:
/// <code>dotnet run --project src/Ashfall.Headless -c Release -- --hash-out tests/Ashfall.Determinism.Tests/Golden/proving-ground.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/proving-ground.checkpoints</code>
/// and say why in the commit message. Never regenerate them to make an unexplained failure go away.
/// </remarks>
public class ProvingGroundGoldenTests
{
    [Fact]
    public async Task Final_state_hash_matches_the_golden_file()
    {
        var result = await RunProvingGroundAsync();
        var golden = (await File.ReadAllTextAsync(GoldenPath("proving-ground.hash"), TestContext.Current.CancellationToken)).Trim();

        Assert.Equal(golden, result.FinalStateHashText);
    }

    [Fact]
    public async Task Every_checkpoint_matches_the_golden_file()
    {
        var result = await RunProvingGroundAsync();
        var golden = await File.ReadAllLinesAsync(GoldenPath("proving-ground.checkpoints"), TestContext.Current.CancellationToken);
        var expected = golden
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(' '))
            .Select(parts => new HashCheckpoint(
                long.Parse(parts[0], CultureInfo.InvariantCulture),
                ulong.Parse(parts[1].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)))
            .ToList();

        Assert.Equal(expected.Count, result.Checkpoints.Count);

        // Report the first divergent tick: that is where a desync investigation starts.
        var first = Enumerable.Range(0, expected.Count).FirstOrDefault(i => expected[i] != result.Checkpoints[i], -1);
        if (first >= 0)
        {
            Assert.Fail(
                $"First divergence at tick {expected[first].Tick}: expected {StateHasher.Format(expected[first].StateHash)}, " +
                $"got {StateHasher.Format(result.Checkpoints[first].StateHash)}.");
        }
    }

    private static async Task<MatchResult> RunProvingGroundAsync()
    {
        await using var match = await HeadlessMatch.StartAsync(
            MatchSpec.ProvingGround(ProvingGround.DefaultSeed), GameSpeed.Unthrottled);
        return await match.RunTicksAsync(ProvingGround.DefaultTicks, TestContext.Current.CancellationToken);
    }

    private static string GoldenPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Golden", fileName);
}
