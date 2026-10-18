using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>Weapon state: ticks until the next shot and the current target (or <see cref="Entity.None"/>).</summary>
public record struct Armament(int Cooldown, Entity Target) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Cooldown);
        hasher.Add(Target.Index);
        hasher.Add(Target.Generation);
    }
}
