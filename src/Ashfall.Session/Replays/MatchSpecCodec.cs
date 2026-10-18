using Ashfall.Core;
using Ashfall.Networking.Wire;
using Ashfall.Simulation;

namespace Ashfall.Session.Replays;

/// <summary>Writes and reads a <see cref="MatchSpec"/> in the binary format replays use.</summary>
public static class MatchSpecCodec
{
    public static void Write(PacketWriter writer, MatchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(spec);
        writer.WriteUInt64(spec.Seed);
        writer.WriteByte((byte)spec.Mode);
        writer.WriteInt32(spec.Map.Width);
        writer.WriteInt32(spec.Map.Height);
        writer.WriteInt32(spec.Players);
        writer.WriteInt32(spec.UnitsPerPlayer);
        writer.WriteInt64(spec.UnitSpeedPerTick.RawValue);
        writer.WriteInt32(spec.OrderIntervalTicks);
        writer.WriteInt32(spec.OrdersPerInterval);
        writer.WriteInt32(spec.InputDelay);
        writer.WriteInt32(spec.Controllers.Count);
        foreach (var controller in spec.Controllers)
        {
            writer.WriteByte((byte)controller);
        }

        writer.WriteInt32(spec.Teams.Count);
        foreach (var team in spec.Teams)
        {
            writer.WriteInt32(team);
        }
    }

    public static MatchSpec Read(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var seed = reader.ReadUInt64();
        var mode = (MatchMode)reader.ReadByte();
        var map = new MapBounds(reader.ReadInt32(), reader.ReadInt32());
        var players = reader.ReadInt32();
        var units = reader.ReadInt32();
        var speed = Fix64.FromRaw(reader.ReadInt64());
        var interval = reader.ReadInt32();
        var orders = reader.ReadInt32();
        var delay = reader.ReadInt32();
        var controllers = new PlayerController[reader.ReadCount(8)];
        for (var i = 0; i < controllers.Length; i++)
        {
            controllers[i] = (PlayerController)reader.ReadByte();
        }

        var teams = new int[reader.ReadCount(8)];
        for (var i = 0; i < teams.Length; i++)
        {
            teams[i] = reader.ReadInt32();
        }

        var spec = new MatchSpec
        {
            Seed = seed,
            Mode = mode,
            Map = map,
            Players = players,
            UnitsPerPlayer = units,
            UnitSpeedPerTick = speed,
            OrderIntervalTicks = interval,
            OrdersPerInterval = orders,
            InputDelay = delay,
            Controllers = controllers,
            Teams = teams,
        };
        try
        {
            spec.Validate();
        }
        catch (ArgumentException error)
        {
            throw new ProtocolException("The match settings are invalid: " + error.Message, error);
        }

        return spec;
    }
}
