using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>
/// Marks an infantry unit sheltering in a bunker: it stands at the bunker's middle, cannot be targeted or ordered
/// to move, and fires out (see <see cref="Systems.GarrisonSystem"/>) until it is unloaded or the bunker falls.
/// </summary>
public record struct Garrisoned(Entity Bunker) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Bunker.Index);
        hasher.Add(Bunker.Generation);
    }
}
