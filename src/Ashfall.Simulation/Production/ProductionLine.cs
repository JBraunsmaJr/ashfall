using System.Runtime.CompilerServices;
using Ashfall.Core;

namespace Ashfall.Simulation.Production;

/// <summary>A fixed-capacity FIFO of unit type indices, stored inline so it fits in an unmanaged component.</summary>
public struct ProductionLine
{
    public const int Capacity = 5;

    private Slots _slots;

    public int Count { get; private set; }

    public readonly bool IsEmpty => Count == 0;

    public readonly bool IsFull => Count == Capacity;

    public readonly int Peek() =>
        Count > 0 ? _slots[0] : throw new InvalidOperationException("The production queue is empty.");

    public bool TryEnqueue(int unitType)
    {
        if (IsFull)
        {
            return false;
        }

        _slots[Count++] = unitType;
        return true;
    }

    public int Dequeue()
    {
        var head = Peek();
        for (var i = 1; i < Count; i++)
        {
            _slots[i - 1] = _slots[i];
        }

        _slots[--Count] = 0;
        return head;
    }

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Count);
        for (var i = 0; i < Count; i++)
        {
            hasher.Add(_slots[i]);
        }
    }

    [InlineArray(Capacity)]
    private struct Slots
    {
        private int _element;
    }
}
