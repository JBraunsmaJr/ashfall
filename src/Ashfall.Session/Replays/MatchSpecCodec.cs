using Ashfall.Core;
using Ashfall.MapGen;
using Ashfall.Networking.Wire;
using Ashfall.Simulation;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Players;

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

        // Format 4: the swarm's nest count.
        writer.WriteInt32(spec.Nests);

        // Format 5: the director's difficulty.
        writer.WriteByte((byte)spec.Difficulty);

        // Format 6: campaign mission settings: starting nest tier and the players' starting bonuses.
        writer.WriteByte((byte)spec.NestTier);
        writer.WriteInt32(spec.BonusCredits);
        writer.WriteByte((byte)spec.BonusHarvesters);

        // Format 7: the players' campaign loadouts, nests revealed and the telegraph bonus.
        writer.WriteInt32(spec.Loadouts.Count);
        foreach (var loadout in spec.Loadouts)
        {
            var modifiers = loadout.Modifiers;
            writer.WriteInt32(modifiers.DamagePercent);
            writer.WriteInt32(modifiers.UnitHealthPercent);
            writer.WriteInt32(modifiers.StructureHealthPercent);
            writer.WriteInt32(modifiers.HarvesterHealthPercent);
            writer.WriteInt32(modifiers.RepairCostPercent);
            writer.WriteInt32(loadout.Credits);
            writer.WriteByte((byte)loadout.Harvesters);
            writer.WriteInt32(loadout.Units.Count);
            foreach (var unit in loadout.Units)
            {
                writer.WriteString(unit);
            }
        }

        writer.WriteBool(spec.RevealNests);
        writer.WriteInt32(spec.TelegraphBonusTicks);
    }

    /// <param name="formatVersion">
    /// The replay format: 1 had no map code (always a hand-made map), 2 no creature count, 3 no nest count, 4 no
    /// difficulty (Veteran), 5 no campaign settings (tier 1 nests, no bonuses), 6 no loadouts.
    /// </param>
    public static MatchSpec Read(PacketReader reader, int formatVersion = 7)
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
        var nests = formatVersion >= 4 ? reader.ReadInt32() : 0;
        var difficulty = formatVersion >= 5 ? (MawDifficulty)reader.ReadByte() : MawDifficulty.Veteran;
        var nestTier = formatVersion >= 6 ? reader.ReadByte() : 1;
        var bonusCredits = formatVersion >= 6 ? reader.ReadInt32() : 0;
        var bonusHarvesters = formatVersion >= 6 ? reader.ReadByte() : 0;
        var loadouts = new List<PlayerLoadout>();
        var revealNests = false;
        var telegraphBonus = 0;
        if (formatVersion >= 7)
        {
            var count = reader.ReadCount(8);
            for (var i = 0; i < count; i++)
            {
                var modifiers = new PlayerModifiers(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                var credits = reader.ReadInt32();
                var harvesters = reader.ReadByte();
                var detachment = new string[reader.ReadCount(PlayerLoadout.MaxUnits)];
                for (var u = 0; u < detachment.Length; u++)
                {
                    detachment[u] = reader.ReadString();
                }

                loadouts.Add(new PlayerLoadout(modifiers, credits, harvesters, detachment));
            }

            revealNests = reader.ReadBool();
            telegraphBonus = reader.ReadInt32();
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
            GeneratedMap = generated,
            Creatures = creatures,
            Nests = nests,
            Difficulty = difficulty,
            NestTier = nestTier,
            BonusCredits = bonusCredits,
            BonusHarvesters = bonusHarvesters,
            Loadouts = loadouts,
            RevealNests = revealNests,
            TelegraphBonusTicks = telegraphBonus,
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
