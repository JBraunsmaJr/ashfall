using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>Hit points and armour. Anything with Health can be targeted and destroyed.</summary>
public record struct Health(int Current, int Max, ArmorType Armor) : IStateHashable
{
    public readonly bool IsDead => Current <= 0;

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Current);
        hasher.Add(Max);
        hasher.Add((int)Armor);
    }
}
