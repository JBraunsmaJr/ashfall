using Ashfall.Core;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Orders;

namespace Ashfall.Simulation.Components;

/// <summary>
/// Weapon state: ticks until the next shot and the current target (or <see cref="Entity.None"/>), with the unit's
/// stance and, while guarding, the point it chases from and returns to.
/// </summary>
public record struct Armament(int Cooldown, Entity Target) : IStateHashable
{
    public UnitStance Stance { get; set; }

    /// <summary>Guard stance: where the unit stood when it went after something (valid while <see cref="Guarding"/>).</summary>
    public FixVec2 GuardPoint { get; set; }

    /// <summary>Guard stance: the unit has left its post to fight and will return to <see cref="GuardPoint"/>.</summary>
    public bool Guarding { get; set; }

    /// <summary>Guard stance: on its way back to <see cref="GuardPoint"/>; it picks no new fights until it is there.</summary>
    public bool Returning { get; set; }

    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Cooldown);
        hasher.Add(Target.Index);
        hasher.Add(Target.Generation);
        hasher.Add((int)Stance);
        hasher.Add(Guarding);
        hasher.Add(Returning);
        if (Guarding)
        {
            hasher.Add(GuardPoint);
        }
    }
}
