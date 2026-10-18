using System.Globalization;
using System.Text;
using Ashfall.Core;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;

namespace Ashfall.Session.Network;

/// <summary>
/// A plain-text listing of the whole simulation state, written when peers desync. Diffing two peers'
/// dumps for the same tick shows exactly which entity or player diverged.
/// </summary>
public static class StateDump
{
    public static string Describe(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;
        text.Append(invariant, $"tick {simulation.Tick} hash {StateHasher.Format(simulation.ComputeStateHash())} rejected {simulation.RejectedCommands}\n");
        text.Append(invariant, $"random {simulation.Random.Snapshot.State:x16}/{simulation.Random.Snapshot.Increment:x16}\n");
        foreach (var player in simulation.Players)
        {
            text.Append(invariant, $"player {player.Id} team {player.Team} credits {player.Credits} power {player.PowerSupply}/{player.PowerDemand} defeated {player.Defeated} site {player.ConstructionSite}\n");
        }

        text.Append(invariant, $"map cindrite {simulation.Map.TotalCindrite}\n");
        var world = simulation.World;
        for (var index = 0; index < world.Capacity; index++)
        {
            var entity = world.EntityAt(index);
            if (!world.IsAlive(entity))
            {
                continue;
            }

            text.Append(invariant, $"{entity}:");
            Append<Position>(text, world, index);
            Append<Owner>(text, world, index);
            Append<Health>(text, world, index);
            Append<UnitInfo>(text, world, index);
            Append<StructureInfo>(text, world, index);
            Append<Mover>(text, world, index);
            Append<Armament>(text, world, index);
            Append<UnitOrder>(text, world, index);
            Append<Harvester>(text, world, index);
            Append<Producer>(text, world, index);
            text.Append('\n');
        }

        return text.ToString();
    }

    private static void Append<T>(StringBuilder text, Simulation.Ecs.World world, int index)
        where T : unmanaged, Simulation.Ecs.IStateHashable
    {
        var store = world.Store<T>();
        if (store.Has(index))
        {
            text.Append(' ').Append(store.Get(index).ToString());
        }
    }
}
