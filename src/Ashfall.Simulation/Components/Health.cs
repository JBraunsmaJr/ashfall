using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>Hit points and armour. Anything with Health can be targeted and destroyed.</summary>
public record struct Health(int Current, int Max, ArmorType Armor) : IStateHashable
{
    public readonly bool IsDead => Current <= 0;

    /// <summary>
    /// Who hit it last and has not been answered yet: an idle unit or a creature turns on its attacker even when the
    /// attacker outranges its own eyes. Cleared once the hit has been considered.
    /// </summary>
    public Entity LastAttacker { get; set; }

    /// <summary>Takes a hit of <paramref name="damage"/> from <paramref name="attacker"/>.</summary>
    public void Hit(int damage, Entity attacker)
    {
        Current -= damage;
        LastAttacker = attacker;
    }

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Current);
        hasher.Add(Max);
        hasher.Add((int)Armor);
        if (!LastAttacker.IsNone)
        {
            hasher.Add(LastAttacker.Index);
            hasher.Add(LastAttacker.Generation);
        }
    }
}
