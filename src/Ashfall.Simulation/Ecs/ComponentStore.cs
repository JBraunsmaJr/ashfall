using Ashfall.Core;

namespace Ashfall.Simulation.Ecs;

/// <summary>Type-erased view of a component store, used by <see cref="World"/> for removal and hashing.</summary>
internal interface IComponentStore
{
    bool Has(int entityIndex);

    bool Remove(int entityIndex);

    void HashEntity(int entityIndex, ref StateHasher hasher);
}

/// <summary>
/// Sparse-set storage for one component type. Components sit densely packed for fast iteration;
/// the order of the dense arrays depends only on the sequence of add and remove calls, so identical
/// command streams produce identical iteration order on every peer.
/// </summary>
public sealed class ComponentStore<T> : IComponentStore
    where T : unmanaged, IStateHashable
{
    private const int InitialCapacity = 16;

    // Entity index -> dense slot + 1 (0 means absent).
    private int[] _sparse = new int[InitialCapacity];
    private int[] _entityIndices = new int[InitialCapacity];
    private T[] _components = new T[InitialCapacity];
    private int _count;

    public int Count => _count;

    /// <summary>Entity indices in dense order, parallel to <see cref="Components"/>.</summary>
    public ReadOnlySpan<int> EntityIndices => _entityIndices.AsSpan(0, _count);

    /// <summary>Components in dense order. Mutate in place; do not add or remove while iterating.</summary>
    public Span<T> Components => _components.AsSpan(0, _count);

    public bool Has(int entityIndex) =>
        (uint)entityIndex < (uint)_sparse.Length && _sparse[entityIndex] != 0;

    /// <summary>
    /// Position of the entity's component in the dense arrays (its place in iteration order), or -1 if it has none.
    /// Lets a search over a subset of entities visit them in the same order a full iteration would.
    /// </summary>
    public int SlotOf(int entityIndex) => Has(entityIndex) ? _sparse[entityIndex] - 1 : -1;

    /// <summary>Reference to the component of <paramref name="entityIndex"/>; throws if it has none.</summary>
    public ref T Get(int entityIndex)
    {
        if (!Has(entityIndex))
        {
            throw new InvalidOperationException($"Entity index {entityIndex} has no {typeof(T).Name}.");
        }

        return ref _components[_sparse[entityIndex] - 1];
    }

    /// <summary>Adds the component, or replaces it if the entity already has one.</summary>
    public void Set(int entityIndex, in T component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityIndex);

        if (Has(entityIndex))
        {
            _components[_sparse[entityIndex] - 1] = component;
            return;
        }

        EnsureSparseCapacity(entityIndex + 1);
        if (_count == _components.Length)
        {
            Array.Resize(ref _components, _count * 2);
            Array.Resize(ref _entityIndices, _count * 2);
        }

        _components[_count] = component;
        _entityIndices[_count] = entityIndex;
        _count++;
        _sparse[entityIndex] = _count;
    }

    /// <summary>Removes the component by moving the last dense element into its slot.</summary>
    public bool Remove(int entityIndex)
    {
        if (!Has(entityIndex))
        {
            return false;
        }

        var slot = _sparse[entityIndex] - 1;
        var last = _count - 1;
        if (slot != last)
        {
            _components[slot] = _components[last];
            _entityIndices[slot] = _entityIndices[last];
            _sparse[_entityIndices[slot]] = slot + 1;
        }

        _components[last] = default;
        _sparse[entityIndex] = 0;
        _count--;
        return true;
    }

    void IComponentStore.HashEntity(int entityIndex, ref StateHasher hasher) => Get(entityIndex).AddTo(ref hasher);

    private void EnsureSparseCapacity(int required)
    {
        if (required <= _sparse.Length)
        {
            return;
        }

        var size = _sparse.Length;
        while (size < required)
        {
            size *= 2;
        }

        Array.Resize(ref _sparse, size);
    }
}
