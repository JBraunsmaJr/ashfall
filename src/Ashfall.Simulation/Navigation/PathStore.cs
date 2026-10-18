using Ashfall.Core;

namespace Ashfall.Simulation.Navigation;

/// <summary>
/// Waypoints of each moving entity, indexed by entity index. Variable-length data lives here rather than
/// in a component so components stay small unmanaged structs. Part of the state hash.
/// </summary>
public sealed class PathStore
{
    private List<FixVec2>?[] _paths = new List<FixVec2>?[64];

    /// <summary>The waypoint list for an entity, created on first use.</summary>
    public List<FixVec2> For(int entityIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityIndex);
        if (entityIndex >= _paths.Length)
        {
            var size = _paths.Length;
            while (size <= entityIndex)
            {
                size *= 2;
            }

            Array.Resize(ref _paths, size);
        }

        return _paths[entityIndex] ??= [];
    }

    public int CountFor(int entityIndex) =>
        entityIndex >= 0 && entityIndex < _paths.Length ? _paths[entityIndex]?.Count ?? 0 : 0;

    public void Clear(int entityIndex)
    {
        if (entityIndex >= 0 && entityIndex < _paths.Length)
        {
            _paths[entityIndex]?.Clear();
        }
    }

    public void HashInto(ref StateHasher hasher)
    {
        for (var index = 0; index < _paths.Length; index++)
        {
            var path = _paths[index];
            if (path is null || path.Count == 0)
            {
                continue;
            }

            hasher.Add(index);
            hasher.Add(path.Count);
            foreach (var point in path)
            {
                hasher.Add(point);
            }
        }
    }
}
