using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Advances structures under construction (slowed by low power). On completion, delivers any free unit
/// (the refinery's harvester) and frees the owner to start the next structure.
/// </summary>
public sealed class ConstructionSystem : ISystem
{
    private readonly List<int> _completed = [];

    public void Update(in TickContext context)
    {
        var structures = context.World.Store<StructureInfo>();
        var owners = context.World.Store<Owner>();
        var indices = structures.EntityIndices;
        var infos = structures.Components;
        _completed.Clear();

        for (var i = 0; i < infos.Length; i++)
        {
            ref var info = ref infos[i];
            if (info.IsComplete)
            {
                continue;
            }

            var playerId = owners.Get(indices[i]).PlayerId;
            if (playerId >= 0 && playerId < context.Players.Count
                && context.Players[playerId].HasLowPower
                && context.Tick % context.Content.Economy.LowPowerSlowdown != 0)
            {
                continue;
            }

            info.BuildRemaining--;
            if (info.IsComplete)
            {
                _completed.Add(indices[i]);
            }
        }

        // Spawning creates entities, so it happens after the loop over the structure store.
        foreach (var index in _completed)
        {
            Complete(context, context.World.EntityAt(index));
        }
    }

    private static void Complete(in TickContext context, Entity structure)
    {
        var info = context.World.Get<StructureInfo>(structure);
        var playerId = context.World.Get<Owner>(structure).PlayerId;
        if (playerId >= 0 && playerId < context.Players.Count && context.Players[playerId].ConstructionSite == structure)
        {
            context.Players[playerId].ConstructionSite = Entity.None;
        }

        var freeUnit = context.Content.Structure(info.StructureType).FreeUnitId;
        if (freeUnit is not null)
        {
            context.Simulation.SpawnUnitAt(structure, context.Content.UnitIndex(freeUnit));
        }
    }
}
