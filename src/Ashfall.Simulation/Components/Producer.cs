using Ashfall.Core;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Production;

namespace Ashfall.Simulation.Components;

/// <summary>
/// A structure's unit queue (up to <see cref="ProductionLine.Capacity"/> unit types, paid for when queued),
/// the ticks left on the unit at its head, and its rally point.
/// </summary>
/// <remarks>
/// The queue is a private field changed only through these methods: a struct exposed as a property would
/// be copied on access, and changes to the copy would be silently lost.
/// </remarks>
public record struct Producer : IStateHashable
{
    private ProductionLine _line;

    public int Remaining { get; set; }

    /// <summary>Where new units go once they are out (valid while <see cref="HasRally"/>).</summary>
    public FixVec2 Rally { get; set; }

    /// <summary>Whether a rally point is set; without one, new units wait at the door.</summary>
    public bool HasRally { get; set; }

    public readonly int QueueCount => _line.Count;

    public readonly bool IsIdle => _line.IsEmpty;

    public readonly bool IsFull => _line.IsFull;

    /// <summary>The unit type being built now.</summary>
    public readonly int Current => _line.Peek();

    /// <summary>How many of <paramref name="unitType"/> are queued, including the one being built.</summary>
    public readonly int CountOf(int unitType) => _line.CountOf(unitType);

    public bool TryEnqueue(int unitType) => _line.TryEnqueue(unitType);

    public int Dequeue() => _line.Dequeue();

    public readonly void AddTo(ref StateHasher hasher)
    {
        _line.AddTo(ref hasher);
        hasher.Add(Remaining);
        if (HasRally)
        {
            hasher.Add(Rally);
        }
    }
}
