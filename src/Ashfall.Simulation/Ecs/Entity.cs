namespace Ashfall.Simulation.Ecs;

/// <summary>
/// A handle to an entity. <see cref="Index"/> slots are reused after destruction; the
/// <see cref="Generation"/> makes stale handles (for example in a delayed command) detectably dead.
/// </summary>
public readonly record struct Entity(int Index, int Generation) : IComparable<Entity>
{
    public static Entity None => new(-1, 0);

    public bool IsNone => Index < 0;

    public int CompareTo(Entity other) =>
        Index != other.Index ? Index.CompareTo(other.Index) : Generation.CompareTo(other.Generation);

    public static bool operator <(Entity left, Entity right) => left.CompareTo(right) < 0;

    public static bool operator >(Entity left, Entity right) => left.CompareTo(right) > 0;

    public static bool operator <=(Entity left, Entity right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Entity left, Entity right) => left.CompareTo(right) >= 0;

    public override string ToString() => IsNone ? "Entity(None)" : $"Entity({Index}v{Generation})";
}
