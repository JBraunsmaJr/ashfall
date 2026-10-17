using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>World position in cell units (cell (0,0) spans 0..1 on both axes).</summary>
public record struct Position(FixVec2 Value) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher) => hasher.Add(Value);
}
