using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Removes everything with no hit points left: frees a structure's footprint (and puts a bunker's occupants
/// outside), drops the owner's construction slot if it was the site under construction, and clears any route.
/// </summary>
public sealed class DeathSystem : ISystem
{
    private readonly List<int> _dead = [];

    public void Update(in TickContext context)
    {
        var world = context.World;
        var healths = world.Store<Health>();
        var indices = healths.EntityIndices;
        var values = healths.Components;
        _dead.Clear();
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].IsDead)
            {
                _dead.Add(indices[i]);
            }
        }

        _dead.Sort();
        foreach (var index in _dead)
        {
            var entity = world.EntityAt(index);
            if (world.Has<StructureInfo>(entity))
            {
                var info = world.Get<StructureInfo>(entity);
                var definition = context.Content.Structure(info.StructureType);
                context.Map.ClearStructure(info.Origin, definition.Width, definition.Height);
                if (definition.Garrison > 0)
                {
                    context.Simulation.EjectGarrison(entity); // the shelter falls; the people in it get out
                }

                var owner = world.Get<Owner>(entity).PlayerId;
                if (owner >= 0 && owner < context.Players.Count && context.Players[owner].ConstructionSite == entity)
                {
                    context.Players[owner].ConstructionSite = Entity.None;
                }
            }

            if (world.Has<Creature>(entity) && context.Simulation.Maw is { } maw)
            {
                maw.Killed++;
            }

            context.Navigator.Paths.Clear(index);
            world.Destroy(entity);
        }
    }
}
