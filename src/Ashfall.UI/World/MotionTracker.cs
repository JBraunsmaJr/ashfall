using System.Numerics;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.UI.World;

/// <summary>
/// Remembers where every unit was on the previous tick and the current one, so frames between ticks
/// can interpolate smoothly, and turns tick-to-tick changes into visual events: shots fired, units and
/// structures destroyed. Presentation only: it reads the simulation, never writes to it.
/// </summary>
internal sealed class MotionTracker
{
    private readonly Dictionary<Entity, Track> _tracks = [];
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
            var entity = world.EntityAt(index);
            var position = ToVector(world.Store<Position>().Get(index).Value);
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

        // Turrets and shots.
        var armaments = world.Store<Armament>();
        foreach (var index in armaments.EntityIndices)
        {
            var entity = world.EntityAt(index);
            var armament = armaments.Get(index);
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

            var weapon = simulation.Content.Unit(world.Store<UnitInfo>().Get(index).UnitType).Weapon;
            if (weapon is not null && armament.Cooldown == weapon.CooldownTicks)
            {
                Shots.Add(new ShotEvent(entity, shooter, aim, world.Store<UnitInfo>().Get(index).UnitType, world.Store<Owner>().Get(index).PlayerId));
            }
        }

        // Units that disappeared this tick died (the simulation never removes units any other way).
        _gone.Clear();
        foreach (var (entity, track) in _tracks)
        {
            if (!world.IsAlive(entity))
            {
                _gone.Add(entity);
                Deaths.Add(new DeathEvent(track.Current, 0.5f, false));
            }
        }

        foreach (var entity in _gone)
        {
            _tracks.Remove(entity);
        }

        _gone.Clear();
        foreach (var (entity, structure) in _structures)
        {
            if (!world.IsAlive(entity))
            {
                _gone.Add(entity);
                Deaths.Add(new DeathEvent(structure.Center, structure.Size, true));
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

    // Turrets swing back toward the hull's heading a little each tick when idle.
    private static float Turn(float from, float to)
    {
        var delta = MathF.IEEERemainder(to - from, MathF.Tau);
        return from + (delta * 0.25f);
    }

    private readonly record struct Track(Vector2 Previous, Vector2 Current, float Heading, float TurretHeading, long Stamp, bool Aiming);
}

/// <summary>A weapon fired this tick.</summary>
internal readonly record struct ShotEvent(Entity Shooter, Vector2 From, Vector2 To, int UnitType, int Owner);

/// <summary>Something was destroyed this tick.</summary>
internal readonly record struct DeathEvent(Vector2 Position, float Size, bool IsStructure);
