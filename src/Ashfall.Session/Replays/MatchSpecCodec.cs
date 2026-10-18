using Ashfall.Core;
using Ashfall.MapGen;
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

        // Format 2: the generated map's code, empty for a hand-made map.
        writer.WriteString(spec.GeneratedMap?.ToCode() ?? string.Empty);

        // Format 3: the swarm's creature count (0 outside swarm matches).
        writer.WriteInt32(spec.Creatures);
    }

    /// <param name="formatVersion">The replay format: 1 had no map code (always a hand-made map), 2 no creature count.</param>
    public static MatchSpec Read(PacketReader reader, int formatVersion = 3)
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

        MapSettings? generated = null;
        if (formatVersion >= 2)
        {
            var code = reader.ReadString();
            if (code.Length > 0)
            {
                generated = MapSettings.TryParseCode(code, out var settings)
                    ? settings
                    : throw new ProtocolException($"Unknown map code {code}.");
            }
        }

        var creatures = formatVersion >= 3 ? reader.ReadInt32() : 0;
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
            GeneratedMap = generated,
            Creatures = creatures,
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
