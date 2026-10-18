using Ashfall.Core;

namespace Ashfall.Content;

/// <summary>
/// A Maw creature type (GDD section 3). Creatures are never built or ordered by a player: the Maw's
/// spawner creates them and the swarm system moves them along flow fields toward player structures.
/// </summary>
/// <param name="SpeedPerTick">Cells moved per tick (15 ticks per second).</param>
/// <param name="Weapon">The creature's attack. Range is measured to the edge of the target, as for units.</param>
/// <param name="AggroRange">How far the creature notices player units and turns to attack them, in cells.</param>
/// <param name="BiomassCost">What one creature costs the Maw's spawning budget.</param>
public sealed record CreatureDefinition(
    string Id,
    string Name,
    int Health,
    ArmorType Armor,
    Fix64 SpeedPerTick,
    WeaponDefinition Weapon,
    Fix64 AggroRange,
    int BiomassCost);
