using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.UI.World;

/// <summary>
/// Remembers where every unit and Maw creature was on the previous tick and the current one, so frames between
/// ticks can interpolate smoothly, and turns tick-to-tick changes into visual events: shots fired (units' weapons
/// and creatures' ranged attacks), and units, creatures and structures destroyed. Presentation only: it reads the
/// simulation, never writes to it.
/// </summary>
internal sealed class MotionTracker
{
    private readonly Dictionary<Entity, Track> _tracks = [];
    private readonly HashSet<Entity> _creatures = [];
    private readonly Dictionary<Entity, (Vector2 Center, float Size)> _structures = [];
    private readonly List<Entity> _gone = [];

    public List<ShotEvent> Shots { get; } = [];

    public List<DeathEvent> Deaths { get; } = [];

    /// <summary>Call after every simulation tick.</summary>
    public void Capture(GameSimulation simulation)
    {
        var world = simulation.World;
        foreach (var index in world.Store<UnitInfo>().EntityIndices)
        {
            Follow(simulation, world.EntityAt(index), index);
        }

        foreach (var index in world.Store<Creature>().EntityIndices)
        {
            var entity = world.EntityAt(index);
            Follow(simulation, entity, index);
            _creatures.Add(entity);
            CreatureShot(simulation, entity, index);
        }

        // Turrets and shots.
        var armaments = world.Store<Armament>();
        foreach (var index in armaments.EntityIndices)
        {
            var entity = world.EntityAt(index);
            var armament = armaments.Get(index);
            if (world.Store<StructureInfo>().Has(index))
            {
                // A guard tower: shots from its middle.
                var tower = simulation.WeaponOf(entity);
                if (tower is not null && !armament.Target.IsNone && world.IsAlive(armament.Target) && armament.Cooldown == tower.CooldownTicks)
                {
                    var from = ToVector(world.Store<Position>().Get(index).Value);
                    var at = ToVector(simulation.ClosestPointOf(armament.Target, world.Store<Position>().Get(index).Value));
                    Shots.Add(new ShotEvent(entity, from, at, tower.Warhead, world.Store<Owner>().Get(index).PlayerId, ToFloat(tower.SplashRadius)));
                }

                continue;
            }

            if (!_tracks.TryGetValue(entity, out var track) || armament.Target.IsNone || !world.IsAlive(armament.Target))
            {
                if (_tracks.TryGetValue(entity, out track))
                {
                    _tracks[entity] = track with { TurretHeading = Turn(track.TurretHeading, track.Heading), Aiming = false };
                }

                continue;
            }

            var shooter = track.Current;
            var aim = ToVector(simulation.ClosestPointOf(armament.Target, world.Store<Position>().Get(index).Value));
            var toTarget = aim - shooter;
            var turret = toTarget.LengthSquared() > 1e-6f ? MathF.Atan2(toTarget.Y, toTarget.X) : track.TurretHeading;
            _tracks[entity] = track with { TurretHeading = turret, Aiming = true };

            var weapon = simulation.WeaponOf(entity);
            if (weapon is not null && armament.Cooldown == weapon.CooldownTicks)
            {
                Shots.Add(new ShotEvent(entity, shooter, aim, weapon.Warhead, world.Store<Owner>().Get(index).PlayerId, ToFloat(weapon.SplashRadius)));
            }
        }

        // Units that disappeared this tick died (the simulation never removes units any other way).
        _gone.Clear();
        foreach (var (entity, track) in _tracks)
        {
            if (!world.IsAlive(entity))
            {
                _gone.Add(entity);
                Deaths.Add(new DeathEvent(track.Current, 0.5f, false, _creatures.Contains(entity)));
            }
        }

        foreach (var entity in _gone)
        {
            _tracks.Remove(entity);
            _creatures.Remove(entity);
        }

        _gone.Clear();
        foreach (var (entity, structure) in _structures)
        {
            if (!world.IsAlive(entity))
            {
                _gone.Add(entity);
                Deaths.Add(new DeathEvent(structure.Center, structure.Size, true, false));
            }
        }

        foreach (var entity in _gone)
        {
            _structures.Remove(entity);
        }

        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            var entity = world.EntityAt(index);
            if (!_structures.ContainsKey(entity))
            {
                var definition = simulation.Content.Structure(world.Store<StructureInfo>().Get(index).StructureType);
                _structures[entity] = (ToVector(world.Store<Position>().Get(index).Value), Math.Max(definition.Width, definition.Height) / 2f);
            }
        }
    }

    /// <summary>Where <paramref name="entity"/> is drawn, <paramref name="alpha"/> of the way from the previous tick to the current one.</summary>
    public Vector2 PositionOf(Entity entity, Vector2 fallback, float alpha) =>
        _tracks.TryGetValue(entity, out var track) ? Vector2.Lerp(track.Previous, track.Current, alpha) : fallback;

    public float HeadingOf(Entity entity) => _tracks.TryGetValue(entity, out var track) ? track.Heading : MathF.PI / 4f;

    public float TurretHeadingOf(Entity entity) => _tracks.TryGetValue(entity, out var track) ? track.TurretHeading : HeadingOf(entity);

    public bool IsMoving(Entity entity) =>
        _tracks.TryGetValue(entity, out var track) && (track.Current - track.Previous).LengthSquared() > 1e-6f;

    public static Vector2 ToVector(Core.FixVec2 value) => new(ToFloat(value.X), ToFloat(value.Y));

    public static float ToFloat(Core.Fix64 value) => (float)(value.RawValue / 4294967296.0);

    private void Follow(GameSimulation simulation, Entity entity, int index)
    {
        var position = ToVector(simulation.World.Store<Position>().Get(index).Value);
        if (_tracks.TryGetValue(entity, out var track))
        {
            var moved = position - track.Current;
            var heading = moved.LengthSquared() > 1e-6f ? MathF.Atan2(moved.Y, moved.X) : track.Heading;
            _tracks[entity] = track with { Previous = track.Current, Current = position, Heading = heading, Stamp = simulation.Tick };
        }
        else
        {
            // New units face the viewer (down the screen) until they move.
            _tracks[entity] = new Track(position, position, MathF.PI / 4f, MathF.PI / 4f, simulation.Tick, false);
        }
    }

    // A ranged creature that attacked this tick (its cooldown just reset) fires a visible shot; melee bites do not.
    private void CreatureShot(GameSimulation simulation, Entity entity, int index)
    {
        var world = simulation.World;
        var creature = world.Store<Creature>().Get(index);
        var weapon = simulation.Content.Creature(creature.CreatureType).Weapon;
        if (weapon.Range <= Core.Fix64.One || creature.Cooldown != weapon.CooldownTicks || !world.IsAlive(creature.Target))
        {
            return;
        }

        var from = _tracks[entity].Current;
        var to = ToVector(simulation.ClosestPointOf(creature.Target, world.Store<Position>().Get(index).Value));
        _tracks[entity] = _tracks[entity] with { Heading = MathF.Atan2(to.Y - from.Y, to.X - from.X) };
        Shots.Add(new ShotEvent(entity, from, to, weapon.Warhead, GameSimulation.MawPlayerId, 0f));
    }

    // Turrets swing back toward the hull's heading a little each tick when idle.
    private static float Turn(float from, float to)
    {
        var delta = MathF.IEEERemainder(to - from, MathF.Tau);
        return from + (delta * 0.25f);
    }

    private readonly record struct Track(Vector2 Previous, Vector2 Current, float Heading, float TurretHeading, long Stamp, bool Aiming);
}

/// <summary>A weapon fired this tick: its warhead decides how the shot looks; a splash radius shows as a burst.</summary>
internal readonly record struct ShotEvent(Entity Shooter, Vector2 From, Vector2 To, Warhead Warhead, int Owner, float Splash);

/// <summary>Something was destroyed this tick. Creatures die in a splat, not an explosion.</summary>
internal readonly record struct DeathEvent(Vector2 Position, float Size, bool IsStructure, bool IsCreature);
