using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Players;

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
            var weapon = context.Simulation.WeaponOf(unit)!;
            var position = world.Get<Position>(unit).Value;
            if (!TargetingSystem.InRange(position, context.Simulation.ClosestPointOf(target, position), context.Simulation.ReachOf(unit)))
            {
                continue;
            }

            var impact = context.Simulation.ClosestPointOf(target, position);
            ref var health = ref world.Get<Health>(target);
            health.Hit(DamageTable.Apply(DamageOf(context, unit, weapon), weapon.Warhead, health.Armor), unit);
            armament.Cooldown = weapon.CooldownTicks;
            if (weapon.SplashRadius > Fix64.Zero)
            {
                Splash(context, unit, world.Get<Owner>(unit).PlayerId, target, impact, weapon);
            }
        }
    }

    // A weapon's base damage, raised by its owner's campaign bonuses.
    private static int DamageOf(in TickContext context, Entity shooter, WeaponDefinition weapon)
    {
        var owner = context.World.Get<Owner>(shooter).PlayerId;
        return context.Simulation.IsPlayer(owner) && context.Players[owner].Modifiers.DamagePercent != 100
            ? PlayerModifiers.Apply(weapon.Damage, context.Players[owner].Modifiers.DamagePercent)
            : weapon.Damage;
    }

    // Every other enemy unit or creature within the splash radius of the impact point takes the same hit, in
    // spatial-grid order. Structures are never splashed: flame clears a crowd, it does not raze a base.
    private static void Splash(in TickContext context, Entity shooter, int owner, Entity target, FixVec2 impact, WeaponDefinition weapon)
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
                    health.Hit(DamageTable.Apply(DamageOf(context, shooter, weapon), weapon.Warhead, health.Armor), shooter);
                }
            }
        }
    }
}
