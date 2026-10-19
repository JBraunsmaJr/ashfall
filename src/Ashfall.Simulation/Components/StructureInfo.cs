using Ashfall.Core;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Components;

/// <summary>
/// Marks a structure: its definition, the top-left cell of its footprint, and the construction ticks
/// still to go (0 once built). An unfinished structure counts for victory but provides nothing else.
/// </summary>
public record struct StructureInfo(int StructureType, Cell Origin, int BuildRemaining) : IStateHashable
{
    public readonly bool IsComplete => BuildRemaining == 0;

    /// <summary>Being repaired: the owner pays as it mends, and it stops once whole (see <see cref="Systems.RepairSystem"/>).</summary>
    public bool Repairing { get; set; }

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(StructureType);
        hasher.Add(Origin.X);
        hasher.Add(Origin.Y);
        hasher.Add(BuildRemaining);
        if (Repairing)
        {
            hasher.Add(Repairing);
        }
    }
}
