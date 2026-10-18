using System.Globalization;
using System.Text;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Orders;

namespace Ashfall.Session;

/// <summary>Human-readable per-player status, for headless runs, logs and tuning.</summary>
public static class MatchReport
{
    public static string Describe(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var world = simulation.World;
        var content = simulation.Content;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"tick {simulation.Tick} ({simulation.Tick / 900.0:F1} min), Cindrite left {simulation.Map.TotalCindrite}");
        foreach (var player in simulation.Players)
        {
            var structures = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var units = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var harvestStates = new SortedDictionary<string, int>(StringComparer.Ordinal);

            foreach (var index in world.Store<StructureInfo>().EntityIndices)
            {
                if (world.Store<Owner>().Get(index).PlayerId == player.Id)
                {
                    var info = world.Store<StructureInfo>().Get(index);
                    var name = content.Structure(info.StructureType).Name + (info.IsComplete ? string.Empty : " (building)");
                    structures[name] = structures.GetValueOrDefault(name) + 1;
                }
            }

            foreach (var index in world.Store<UnitInfo>().EntityIndices)
            {
                if (world.Store<Owner>().Get(index).PlayerId != player.Id)
                {
                    continue;
                }

                var name = content.Unit(world.Store<UnitInfo>().Get(index).UnitType).Name;
                units[name] = units.GetValueOrDefault(name) + 1;
                if (world.Store<Harvester>().Has(index))
                {
                    var state = world.Store<Harvester>().Get(index).State.ToString();
                    harvestStates[state] = harvestStates.GetValueOrDefault(state) + 1;
                }
            }

            text.Append(CultureInfo.InvariantCulture,
                $"\n  player {player.Id} (team {player.Team}){(player.Defeated ? " DEFEATED" : string.Empty)}: " +
                $"credits {player.Credits}, power {player.PowerSupply - player.PowerDemand:+#;-#;0} ({player.PowerSupply}/{player.PowerDemand})");
            text.Append("\n    structures: ").Append(Join(structures));
            text.Append("\n    units: ").Append(Join(units));
            if (harvestStates.Count > 0)
            {
                text.Append("\n    harvesters: ").Append(Join(harvestStates));
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"\n  rejected commands: {simulation.RejectedCommands}");
        return text.ToString();
    }

    /// <summary>Counts of units per standing order, useful when diagnosing stuck armies.</summary>
    public static string DescribeOrders(GameSimulation simulation, int playerId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var world = simulation.World;
        var counts = new SortedDictionary<OrderKind, int>();
        foreach (var index in world.Store<UnitOrder>().EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId == playerId)
            {
                var kind = world.Store<UnitOrder>().Get(index).Kind;
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }
        }

        return string.Join(", ", counts.Select(pair => $"{pair.Key} {pair.Value}"));
    }

    private static string Join(SortedDictionary<string, int> counts) =>
        counts.Count == 0 ? "none" : string.Join(", ", counts.Select(pair => $"{pair.Value} {pair.Key}"));
}
