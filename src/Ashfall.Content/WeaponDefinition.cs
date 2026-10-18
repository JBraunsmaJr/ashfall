using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>A weapon. M1 weapons hit instantly; projectiles with travel time come later.</summary>
/// <param name="Damage">Base damage per shot, before the armour multiplier.</param>
/// <param name="Range">Reach in cells, measured to the edge of the target.</param>
/// <param name="CooldownTicks">Ticks between shots (15 ticks = 1 second).</param>
/// <param name="SplashRadius">
/// Area weapons (flame): every other enemy unit or creature within this many cells of the target takes the same hit.
/// Zero for weapons that hit only their target.
/// </param>
public sealed record WeaponDefinition(string Id, Warhead Warhead, int Damage, Fix64 Range, int CooldownTicks, Fix64 SplashRadius = default);
