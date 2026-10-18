using Ashfall.Core;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Production;

namespace Ashfall.Simulation.Components;

/// <summary>
/// A structure's unit queue (up to <see cref="ProductionLine.Capacity"/> unit types, paid for when queued)
/// and the ticks left on the unit at its head.
/// </summary>
/// <remarks>
/// The queue is a private field changed only through these methods: a struct exposed as a property would
/// be copied on access, and changes to the copy would be silently lost.
/// </remarks>
public record struct Producer : IStateHashable
{
    private ProductionLine _line;

    public int Remaining { get; set; }

    public readonly int QueueCount => _line.Count;

    public readonly bool IsIdle => _line.IsEmpty;

    public readonly bool IsFull => _line.IsFull;

    /// <summary>The unit type being built now.</summary>
    public readonly int Current => _line.Peek();

    public bool TryEnqueue(int unitType) => _line.TryEnqueue(unitType);

    public int Dequeue() => _line.Dequeue();

    public readonly void AddTo(ref StateHasher hasher)
    {
        _line.AddTo(ref hasher);
        hasher.Add(Remaining);
    }
}
