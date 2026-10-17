using Ashfall.Core;

namespace Ashfall.Simulation.Ecs;

/// <summary>
/// Entity registry and component stores for the deterministic simulation.
/// </summary>
/// <remarks>
/// Determinism rules enforced here:
/// <list type="bullet">
/// <item>Component types are registered once, in a fixed order, when the simulation is built.</item>
/// <item>Destruction is deferred and applied by <see cref="FlushDestroyed"/> in ascending entity order,
/// so systems can destroy entities while iterating.</item>
/// <item>Freed indices are reused lowest-first.</item>
/// <item>The state hash walks entities by index and stores in registration order, so it is canonical.</item>
/// </list>
/// </remarks>
public sealed class World
{
    private readonly List<int> _generations = [];
    private readonly List<bool> _alive = [];
    private readonly List<bool> _pendingDestroyFlags = [];
    private readonly List<int> _pendingDestroy = [];
    private readonly List<int> _freeIndices = []; // kept sorted descending; lowest index is at the end
    private readonly List<IComponentStore> _stores = [];
    private readonly Dictionary<Type, IComponentStore> _storesByType = [];

    /// <summary>Number of live entities.</summary>
    public int AliveCount { get; private set; }

    /// <summary>Number of entity slots ever allocated.</summary>
    public int Capacity => _generations.Count;

    /// <summary>Registers a component type. Must happen before any entity uses it, in the same order on every peer.</summary>
    public ComponentStore<T> Register<T>()
        where T : unmanaged, IStateHashable
    {
        if (_storesByType.ContainsKey(typeof(T)))
        {
            throw new InvalidOperationException($"{typeof(T).Name} is already registered.");
        }

        var store = new ComponentStore<T>();
        _stores.Add(store);
        _storesByType.Add(typeof(T), store);
        return store;
    }

    /// <summary>The store for a registered component type.</summary>
    public ComponentStore<T> Store<T>()
        where T : unmanaged, IStateHashable =>
        _storesByType.TryGetValue(typeof(T), out var store)
            ? (ComponentStore<T>)store
            : throw new InvalidOperationException($"{typeof(T).Name} is not registered.");

    public Entity Create()
    {
        int index;
        if (_freeIndices.Count > 0)
        {
            index = _freeIndices[^1];
            _freeIndices.RemoveAt(_freeIndices.Count - 1);
            _alive[index] = true;
        }
        else
        {
            index = _generations.Count;
            _generations.Add(0);
            _alive.Add(true);
            _pendingDestroyFlags.Add(false);
        }

        AliveCount++;
        return new Entity(index, _generations[index]);
    }

    public bool IsAlive(Entity entity) =>
        entity.Index >= 0
        && entity.Index < _generations.Count
        && _alive[entity.Index]
        && _generations[entity.Index] == entity.Generation;

    /// <summary>Marks an entity for destruction at the end of the current tick. Ignores dead handles.</summary>
    public void Destroy(Entity entity)
    {
        if (!IsAlive(entity) || _pendingDestroyFlags[entity.Index])
        {
            return;
        }

        _pendingDestroyFlags[entity.Index] = true;
        _pendingDestroy.Add(entity.Index);
    }

    /// <summary>Applies pending destruction in ascending index order.</summary>
    public void FlushDestroyed()
    {
        if (_pendingDestroy.Count == 0)
        {
            return;
        }

        _pendingDestroy.Sort();
        foreach (var index in _pendingDestroy)
        {
            foreach (var store in _stores)
            {
                store.Remove(index);
            }

            _alive[index] = false;
            _pendingDestroyFlags[index] = false;
            _generations[index]++;
            AliveCount--;
            InsertFreeIndex(index);
        }

        _pendingDestroy.Clear();
    }

    public void Add<T>(Entity entity, in T component)
        where T : unmanaged, IStateHashable
    {
        EnsureAlive(entity);
        Store<T>().Set(entity.Index, component);
    }

    public bool Has<T>(Entity entity)
        where T : unmanaged, IStateHashable =>
        IsAlive(entity) && Store<T>().Has(entity.Index);

    public ref T Get<T>(Entity entity)
        where T : unmanaged, IStateHashable
    {
        EnsureAlive(entity);
        return ref Store<T>().Get(entity.Index);
    }

    /// <summary>Handle for a live entity index, or <see cref="Entity.None"/>.</summary>
    public Entity EntityAt(int index) =>
        index >= 0 && index < _alive.Count && _alive[index] ? new Entity(index, _generations[index]) : Entity.None;

    /// <summary>Adds the canonical state of every live entity and component to <paramref name="hasher"/>.</summary>
    public void HashInto(ref StateHasher hasher)
    {
        hasher.Add(AliveCount);
        for (var index = 0; index < _generations.Count; index++)
        {
            if (!_alive[index])
            {
                continue;
            }

            hasher.Add(index);
            hasher.Add(_generations[index]);
            for (var storeOrdinal = 0; storeOrdinal < _stores.Count; storeOrdinal++)
            {
                var store = _stores[storeOrdinal];
                if (store.Has(index))
                {
                    hasher.Add(storeOrdinal);
                    store.HashEntity(index, ref hasher);
                }
            }
        }
    }

    private void InsertFreeIndex(int index)
    {
        // Descending order, so the lowest free index is popped first.
        var position = _freeIndices.Count;
        while (position > 0 && _freeIndices[position - 1] < index)
        {
            position--;
        }

        _freeIndices.Insert(position, index);
    }

    private void EnsureAlive(Entity entity)
    {
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException($"{entity} is not alive.");
        }
    }
}
