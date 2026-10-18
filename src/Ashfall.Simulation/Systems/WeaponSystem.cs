using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Fires every armed unit whose target is in range and whose weapon has cooled down. Weapons hit instantly; damage
/// is the weapon's base damage scaled by the warhead-versus-armour table. Area weapons (a
/// <see cref="WeaponDefinition.SplashRadius"/>) also hit every other enemy unit or creature that close to the target.
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

            var impact = context.Simulation.ClosestPointOf(target, position);
            ref var health = ref world.Get<Health>(target);
            health.Current -= DamageTable.Apply(weapon.Damage, weapon.Warhead, health.Armor);
            armament.Cooldown = weapon.CooldownTicks;
            if (weapon.SplashRadius > Fix64.Zero)
            {
                Splash(context, world.Get<Owner>(unit).PlayerId, target, impact, weapon);
            }
        }
    }

    // Every other enemy unit or creature within the splash radius of the impact point takes the same hit, in
    // spatial-grid order. Structures are never splashed: flame clears a crowd, it does not raze a base.
    private static void Splash(in TickContext context, int owner, Entity target, FixVec2 impact, WeaponDefinition weapon)
    {
        var simulation = context.Simulation;
        var world = context.World;
        var owners = world.Store<Owner>();
        var positions = world.Store<Position>();
        var healths = world.Store<Health>();
        var radiusSquared = weapon.SplashRadius * weapon.SplashRadius;
        var (minX, minY, maxX, maxY) = simulation.Spatial.CellsAround(impact, weapon.SplashRadius);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                foreach (var index in simulation.Spatial.At(x, y))
                {
                    if (index == target.Index
                        || !world.IsAlive(world.EntityAt(index))
                        || !simulation.AreEnemies(owner, owners.Get(index).PlayerId)
                        || FixVec2.DistanceSquared(impact, positions.Get(index).Value) > radiusSquared)
                    {
                        continue;
                    }

                    ref var health = ref healths.Get(index);
                    health.Current -= DamageTable.Apply(weapon.Damage, weapon.Warhead, health.Armor);
                }
            }
        }
    }
}
