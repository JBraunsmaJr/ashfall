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
/// <item><b>generated</b> (M4): a 2v2 between scripted AIs on a generated Riverlands map, played to victory.</item>
/// <item><b>swarm</b> (M5): 1,000 Maw creatures against the Bulwark fortress for one minute.</item>
/// <item><b>nests</b> (M5): four Spawning Pits on regrowing Cindrite breeding against the fortress for four minutes,
/// evolving into Brood Nests that add Spitters.</item>
/// <item><b>purge</b>: the co-op Purge on Recruit, two scripted allies against four nests on a generated Crossroads map,
/// played until the players have destroyed every nest (brooding, the director's probes and assaults, the Purge
/// victory).</item>
/// </list>
/// <c>maps.hashes</c> pins the generator itself: map code, player count and the grid's hash.
/// </summary>
/// <remarks>
/// If a change is meant to alter simulation behaviour, regenerate the golden files with:
/// <code>
/// dotnet run --project src/Ashfall.Headless -c Release -- --hash-out tests/Ashfall.Determinism.Tests/Golden/proving-ground.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/proving-ground.checkpoints
/// dotnet run --project src/Ashfall.Headless -c Release -- --mode skirmish --hash-out tests/Ashfall.Determinism.Tests/Golden/skirmish.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/skirmish.checkpoints
/// dotnet run --project src/Ashfall.Headless -c Release -- --mode skirmish --players 4 --teams 0,1,0,1 --map-code RIV-20000Z-N5NJHJ-TM6XWS --hash-out tests/Ashfall.Determinism.Tests/Golden/generated.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/generated.checkpoints
/// dotnet run --project src/Ashfall.Headless -c Release -- --mode swarm --creatures 1000 --ticks 900 --hash-out tests/Ashfall.Determinism.Tests/Golden/swarm.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/swarm.checkpoints
/// dotnet run --project src/Ashfall.Headless -c Release -- --mode swarm --nests 4 --ticks 3600 --hash-out tests/Ashfall.Determinism.Tests/Golden/nests.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/nests.checkpoints
/// dotnet run --project src/Ashfall.Headless -c Release -- --mode purge --players 2 --seed 1 --difficulty recruit --hash-out tests/Ashfall.Determinism.Tests/Golden/purge.hash --checkpoints-out tests/Ashfall.Determinism.Tests/Golden/purge.checkpoints
/// dotnet run --project tools/Ashfall.Tools.MapPreview -c Release -- --code CODE --players N   (prints the hash for maps.hashes)
/// </code>
/// and say why in the commit message. Never regenerate them to make an unexplained failure go away.
/// </remarks>
public class GoldenReplayTests
{
    /// <summary>The generated-map reference match: a 2v2 on this Riverlands map.</summary>
    public const string GeneratedMapCode = "RIV-20000Z-N5NJHJ-TM6XWS";

    /// <summary>The swarm reference run: this many creatures for this many ticks (a minute of game time).</summary>
    public const int SwarmCreatures = 1_000;

    public const long SwarmTicks = 900;

    /// <summary>The nest reference run: this many Spawning Pits (with the default population limit) for four minutes.</summary>
    public const int NestCount = 4;

    public const long NestTicks = 3_600;

    /// <summary>The Purge reference match: this seed, player count and difficulty (scripted allies who win).</summary>
    public const ulong PurgeSeed = 1;

    public const int PurgePlayers = 2;

    public const Ashfall.Simulation.Horde.MawDifficulty PurgeDifficulty = Ashfall.Simulation.Horde.MawDifficulty.Recruit;

    public static TheoryData<string> Replays => new() { "proving-ground", "skirmish", "generated", "swarm", "nests", "purge" };

    [Fact]
    public void Generated_maps_match_their_golden_hashes()
    {
        var lines = File.ReadAllLines(GoldenPath("maps.hashes")).Select(Clean).Where(line => line.Length > 0).ToList();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(Ashfall.MapGen.MapSettings.TryParseCode(parts[0], out var settings), parts[0]);
            var players = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var hash = Ashfall.MapGen.MapGenerator.Generate(settings, players).Hash;
            Assert.Equal(parts[2], StateHasher.Format(hash));
        }
    }

    [Theory]
    [MemberData(nameof(Replays))]
    public async Task Final_state_hash_matches_the_golden_file(string replay)
    {
        var result = await RunAsync(replay);
        var golden = Clean(await File.ReadAllTextAsync(GoldenPath(replay + ".hash"), TestContext.Current.CancellationToken));

        Assert.Equal(golden, result.FinalStateHashText);
    }

    [Theory]
    [MemberData(nameof(Replays))]
    public async Task Every_checkpoint_matches_the_golden_file(string replay)
    {
        var result = await RunAsync(replay);
        var golden = await File.ReadAllLinesAsync(GoldenPath(replay + ".checkpoints"), TestContext.Current.CancellationToken);
        var expected = golden
            .Select(Clean)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
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
        if (replay == "generated")
        {
            Assert.True(Ashfall.MapGen.MapSettings.TryParseCode(GeneratedMapCode, out var map));
            var spec = MatchSpec.Skirmish(ProvingGround.DefaultSeed, 4).WithTeams(0, 1, 0, 1).WithGeneratedMap(map);
            await using var generated = await HeadlessMatch.StartAsync(spec, GameSpeed.Unthrottled);
            return await generated.RunToCompletionAsync(MatchSpec.SkirmishTickLimit, cancellation);
        }

        if (replay == "purge")
        {
            await using var purge = await HeadlessMatch.StartAsync(MatchSpec.Purge(PurgeSeed, PurgePlayers, difficulty: PurgeDifficulty), GameSpeed.Unthrottled);
            return await purge.RunToCompletionAsync(MatchSpec.SkirmishTickLimit, cancellation);
        }

        if (replay == "nests")
        {
            await using var nests = await HeadlessMatch.StartAsync(MatchSpec.Swarm(ProvingGround.DefaultSeed, nests: NestCount), GameSpeed.Unthrottled);
            return await nests.RunTicksAsync(NestTicks, cancellation);
        }

        if (replay == "swarm")
        {
            await using var swarm = await HeadlessMatch.StartAsync(MatchSpec.Swarm(ProvingGround.DefaultSeed, SwarmCreatures), GameSpeed.Unthrottled);
            return await swarm.RunTicksAsync(SwarmTicks, cancellation);
        }

        if (replay == "skirmish")
        {
            await using var skirmish = await HeadlessMatch.StartAsync(MatchSpec.Skirmish(ProvingGround.DefaultSeed), GameSpeed.Unthrottled);
            return await skirmish.RunToCompletionAsync(MatchSpec.SkirmishTickLimit, cancellation);
        }

        await using var match = await HeadlessMatch.StartAsync(MatchSpec.ProvingGround(ProvingGround.DefaultSeed), GameSpeed.Unthrottled);
        return await match.RunTicksAsync(ProvingGround.DefaultTicks, cancellation);
    }

    // Editors on Windows like to add byte-order marks (sometimes two) and CRLFs; neither is data.
    private static string Clean(string text) => text.Replace("\uFEFF", string.Empty, StringComparison.Ordinal).Trim();

    private static string GoldenPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Golden", fileName);
}
