using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>
/// Marks a Maw creature: its definition (an index into <see cref="Content.GameContent.Creatures"/>), ticks
/// until its next attack, and what it is attacking (a player unit or structure, or <see cref="Entity.None"/>
/// while it follows the swarm's flow field). Creatures have no <see cref="Mover"/>: the swarm system moves them.
/// </summary>
public record struct Creature(int CreatureType, int Cooldown, Entity Target) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(CreatureType);
        hasher.Add(Cooldown);
        hasher.Add(Target.Index);
        hasher.Add(Target.Generation);
    }
}
