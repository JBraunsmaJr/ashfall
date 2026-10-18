using Ashfall.Core;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;

namespace Ashfall.Networking.Wire;

/// <summary>
/// Commands on the wire. Fixed-point values travel as their raw 64-bit integers, so every peer
/// decodes exactly the value that was sent. The player id is not sent: a turn belongs to one seat,
/// and the receiver stamps that seat on every command so a peer cannot speak for someone else.
/// </summary>
public static class CommandCodec
{
    /// <summary>Most commands one turn may carry; more is treated as a malformed packet.</summary>
    public const int MaxCommandsPerTurn = 1024;

    public static void Write(PacketWriter writer, in Command command)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteByte((byte)command.Kind);
        WriteEntity(writer, command.Unit);
        writer.WriteInt64(command.Target.X.RawValue);
        writer.WriteInt64(command.Target.Y.RawValue);
        WriteEntity(writer, command.TargetEntity);
        writer.WriteInt32(command.TypeIndex);
        writer.WriteInt32(command.Cell.X);
        writer.WriteInt32(command.Cell.Y);
    }

    public static Command Read(PacketReader reader, int playerId)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var kind = (CommandKind)reader.ReadByte();
        if (!Enum.IsDefined(kind))
        {
            throw new ProtocolException($"Unknown command kind {(byte)kind}.");
        }

        var unit = ReadEntity(reader);
        var target = new FixVec2(Fix64.FromRaw(reader.ReadInt64()), Fix64.FromRaw(reader.ReadInt64()));
        var targetEntity = ReadEntity(reader);
        var typeIndex = reader.ReadInt32();
        var cell = new Cell(reader.ReadInt32(), reader.ReadInt32());
        return new Command(playerId, kind, unit, target, targetEntity, typeIndex, cell);
    }

    public static void WriteList(PacketWriter writer, IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count > MaxCommandsPerTurn)
        {
            throw new ArgumentException($"A turn carries at most {MaxCommandsPerTurn} commands.", nameof(commands));
        }

        writer.WriteInt32(commands.Count);
        foreach (var command in commands)
        {
            Write(writer, command);
        }
    }

    public static Command[] ReadList(PacketReader reader, int playerId)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var commands = new Command[reader.ReadCount(MaxCommandsPerTurn)];
        for (var i = 0; i < commands.Length; i++)
        {
            commands[i] = Read(reader, playerId);
        }

        return commands;
    }

    private static void WriteEntity(PacketWriter writer, Entity entity)
    {
        writer.WriteInt32(entity.Index);
        writer.WriteInt32(entity.Generation);
    }

    private static Entity ReadEntity(PacketReader reader) => new(reader.ReadInt32(), reader.ReadInt32());
}
