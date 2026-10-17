using Ashfall.Core;

namespace Ashfall.Simulation.Ecs;

/// <summary>A component that contributes its full state to the desync hash.</summary>
public interface IStateHashable
{
    void AddTo(ref StateHasher hasher);
}
