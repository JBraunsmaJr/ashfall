using Ashfall.Content;
using Ashfall.Simulation.Components;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Fires every armed unit whose target is in range and whose weapon has cooled down. M1 weapons hit
/// instantly; damage is the weapon's base damage scaled by the warhead-versus-armour table.
/// </summary>
public sealed class WeaponSystem : ISystem
{
    public void Update(in TickContext context)
    {
        var world = context.World;
        var armaments = world.Store<Armament>();
        var indices = armaments.EntityIndices;
        var weapons = armaments.Components;
        for (var i = 0; i < weapons.Length; i++)
        {
            ref var armament = ref weapons[i];
            if (armament.Cooldown > 0)
            {
                armament.Cooldown--;
            }

            var target = armament.Target;
            if (target.IsNone || armament.Cooldown > 0 || !world.IsAlive(target) || !world.Has<Health>(target))
            {
                continue;
            }

            var unit = world.EntityAt(indices[i]);
            var weapon = context.Content.Unit(world.Get<UnitInfo>(unit).UnitType).Weapon!;
            var position = world.Get<Position>(unit).Value;
            if (!TargetingSystem.InRange(position, context.Simulation.ClosestPointOf(target, position), weapon.Range))
            {
                continue;
            }

            ref var health = ref world.Get<Health>(target);
            health.Current -= DamageTable.Apply(weapon.Damage, weapon.Warhead, health.Armor);
            armament.Cooldown = weapon.CooldownTicks;
        }
    }
}
