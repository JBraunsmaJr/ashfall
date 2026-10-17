using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>The player that controls an entity. Commands from other players are rejected.</summary>
public record struct Owner(int PlayerId) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher) => hasher.Add(PlayerId);
}
