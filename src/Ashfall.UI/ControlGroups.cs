using Ashfall.Simulation.Ecs;

namespace Ashfall.UI;

/// <summary>Control groups 0–9: Ctrl+number stores the selection, number recalls it.</summary>
public sealed class ControlGroups
{
    private readonly List<Entity>[] _groups = Enumerable.Range(0, 10).Select(_ => new List<Entity>()).ToArray();

    public IReadOnlyList<Entity> Get(int group) => _groups[group];

    public void Assign(int group, IEnumerable<Entity> entities)
    {
        _groups[group].Clear();
        _groups[group].AddRange(entities);
    }

    /// <summary>Drops entities that no longer exist.</summary>
    public void Prune(Func<Entity, bool> isAlive)
    {
        foreach (var group in _groups)
        {
            group.RemoveAll(entity => !isAlive(entity));
        }
    }
}
