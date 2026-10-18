using Ashfall.Simulation.Components;

namespace Ashfall.Simulation.Systems;

/// <summary>Totals each player's power supply and demand from completed structures.</summary>
public sealed class PowerSystem : ISystem
{
    public void Update(in TickContext context)
    {
        foreach (var player in context.Players)
        {
            player.PowerSupply = 0;
            player.PowerDemand = 0;
        }

        var structures = context.World.Store<StructureInfo>();
        var owners = context.World.Store<Owner>();
        var indices = structures.EntityIndices;
        var infos = structures.Components;
        for (var i = 0; i < infos.Length; i++)
        {
            var playerId = owners.Get(indices[i]).PlayerId;
            if (!infos[i].IsComplete || playerId < 0 || playerId >= context.Players.Count)
            {
                continue;
            }

            var power = context.Content.Structure(infos[i].StructureType).Power;
            var player = context.Players[playerId];
            if (power >= 0)
            {
                player.PowerSupply += power;
            }
            else
            {
                player.PowerDemand -= power;
            }
        }
    }
}
