using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>
/// Marks a Maw creature: its definition (an index into <see cref="Content.GameContent.Creatures"/>), ticks
/// until its next attack, and what it is attacking (a player unit or structure, or <see cref="Entity.None"/>
/// while it follows the swarm's flow field). Creatures have no <see cref="Mover"/>: the swarm system moves them.
/// </summary>
/// <param name="Home">The nest that bred it, or <see cref="Entity.None"/> for a creature from the map edge.</param>
/// <param name="Brooding">
/// True while it waits around its nest for the Maw's next wave (matches with waves only): it guards the nest, attacking
/// units that come near, and does not march on the players until the wave is launched or its nest dies.
/// </param>
public record struct Creature(int CreatureType, int Cooldown, Entity Target, Entity Home, bool Brooding) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(CreatureType);
        hasher.Add(Cooldown);
        hasher.Add(Target.Index);
        hasher.Add(Target.Generation);
        if (!Home.IsNone)
        {
            // Only bred creatures have a home (and only they brood): edge spawns hash as they always have.
            hasher.Add(Home.Index);
            hasher.Add(Home.Generation);
            hasher.Add(Brooding);
        }
    }
}
