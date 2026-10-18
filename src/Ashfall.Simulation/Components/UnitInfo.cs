using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>Marks a unit and names its definition (an index into <see cref="Content.GameContent.Units"/>).</summary>
public record struct UnitInfo(int UnitType) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher) => hasher.Add(UnitType);
}
