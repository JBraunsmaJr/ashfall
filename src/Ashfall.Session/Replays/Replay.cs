using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Networking.Wire;
using Ashfall.Session.Commands;
using Ashfall.Simulation.Commands;

namespace Ashfall.Session.Replays;

/// <summary>
/// A recorded match: its settings, every command people issued, and the state hash it ended with
/// (GDD section 8). AI players are recomputed, so a 20-minute match is a few kilobytes. Playing a
/// replay must reproduce the final hash exactly; anything else is a determinism bug.
/// </summary>
public sealed class Replay
{
    private static ReadOnlySpan<byte> Magic => "ASHFALL-REPLAY\0\0"u8;

    // 1: first format. 2: generated map code. 3: swarm creature count.
    private const int FormatVersion = 3;

    public Replay(MatchSpec spec, ulong contentHash, IReadOnlyList<(long Tick, Command[] Commands)> commands, long finalTick, ulong finalHash)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(commands);
        Spec = spec;
        ContentHash = contentHash;
        Commands = commands;
        FinalTick = finalTick;
        FinalHash = finalHash;
    }

    /// <summary>The match settings, with every person's seat turned into a <see cref="PlayerController.Remote"/> seat fed by the recording.</summary>
    public MatchSpec Spec { get; }

    public ulong ContentHash { get; }

    public IReadOnlyList<(long Tick, Command[] Commands)> Commands { get; }

    public long FinalTick { get; }

    public ulong FinalHash { get; }

    /// <summary>Captures a match as it stands now.</summary>
    public static Replay From(HeadlessMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var spec = match.Spec;
        if (spec.Mode == MatchMode.Skirmish)
        {
            var controllers = spec.Controllers
                .Select(controller => controller == PlayerController.Human ? PlayerController.Remote : controller)
                .ToArray();
            spec = spec with { Controllers = controllers };
        }

        return new Replay(spec, GameContent.Standard.ComputeHash(), match.Log.Entries, match.Tick, match.Simulation.ComputeStateHash());
    }

    /// <summary>Builds a match that will play the recording back.</summary>
    public HeadlessMatch CreateMatch(GameSpeed speed, TimeProvider? time = null)
    {
        if (ContentHash != GameContent.Standard.ComputeHash())
        {
            throw new InvalidOperationException("This replay was recorded with different game content (another version or mod).");
        }

        var match = HeadlessMatch.Start(Spec, speed, time);
        if (match.Turns is { } turns)
        {
            var byTick = Commands.ToDictionary(entry => entry.Tick, entry => entry.Commands);
            for (var tick = 0L; tick < FinalTick; tick++)
            {
                byTick.TryGetValue(tick, out var commands);
                foreach (var seat in turns.Seats)
                {
                    turns.Set(tick, seat, commands is null ? [] : commands.Where(command => command.PlayerId == seat).ToArray());
                }
            }
        }

        return match;
    }

    /// <summary>Plays the replay to its end; returns the hash reached and whether it matches the recording.</summary>
    public (ulong Hash, bool Matches) Verify()
    {
        var match = CreateMatch(GameSpeed.Unthrottled);
        while (match.Tick < FinalTick)
        {
            match.Step();
        }

        var hash = match.Simulation.ComputeStateHash();
        return (hash, hash == FinalHash);
    }

    public byte[] Encode()
    {
        var writer = new PacketWriter(4096);
        writer.WriteRaw(Magic);
        writer.WriteInt32(FormatVersion);
        writer.WriteUInt64(ContentHash);
        MatchSpecCodec.Write(writer, Spec);
        writer.WriteInt64(FinalTick);
        writer.WriteUInt64(FinalHash);
        writer.WriteInt32(Commands.Count);
        foreach (var (tick, commands) in Commands)
        {
            writer.WriteInt64(tick);
            writer.WriteInt32(commands.Length);
            foreach (var command in commands)
            {
                writer.WriteInt32(command.PlayerId);
                CommandCodec.Write(writer, command);
            }
        }

        return writer.ToArray();
    }

    public static Replay Decode(ReadOnlyMemory<byte> data)
    {
        if (data.Length < Magic.Length || !data.Span[..Magic.Length].SequenceEqual(Magic))
        {
            throw new ProtocolException("Not an Ashfall replay.");
        }

        var reader = new PacketReader(data[Magic.Length..]);
        var version = reader.ReadInt32();
        if (version is < 1 or > FormatVersion)
        {
            throw new ProtocolException($"Replay format {version} is not supported (this build reads 1 to {FormatVersion}).");
        }

        var contentHash = reader.ReadUInt64();
        var spec = MatchSpecCodec.Read(reader, version);
        var finalTick = reader.ReadInt64();
        var finalHash = reader.ReadUInt64();
        var entries = new (long, Command[])[reader.ReadCount(int.MaxValue / 64)];
        for (var i = 0; i < entries.Length; i++)
        {
            var tick = reader.ReadInt64();
            var commands = new Command[reader.ReadCount(CommandCodec.MaxCommandsPerTurn * 8)];
            for (var c = 0; c < commands.Length; c++)
            {
                var player = reader.ReadInt32();
                commands[c] = CommandCodec.Read(reader, player);
            }

            entries[i] = (tick, commands);
        }

        reader.ExpectEnd();
        return new Replay(spec, contentHash, entries, finalTick, finalHash);
    }

    public void Save(string path) => File.WriteAllBytes(path, Encode());

    public static Replay Load(string path) => Decode(File.ReadAllBytes(path));

    public override string ToString() =>
        $"replay: {Spec.Players} players, seed {Spec.Seed}, {FinalTick} ticks, {Commands.Sum(entry => entry.Commands.Length)} commands, ends at {StateHasher.Format(FinalHash)}";
}
