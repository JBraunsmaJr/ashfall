using System.Numerics;
using Ashfall.Content;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.UI.Drawing;

namespace Ashfall.UI.World;

/// <summary>
/// Short-lived visual effects with their lights: shots styled by warhead (tracers, heavy anti-armour rounds, flame
/// bursts, arcs of acid), muzzle flashes, explosions, creature deaths and order markers.
/// </summary>
internal sealed class Effects
{
    private readonly List<Effect> _active = [];

    public int Count => _active.Count;

    public void Shot(ShotEvent shot, double now)
    {
        switch (shot.Warhead)
        {
            case Warhead.Flame:
                // A gout of fire to the target and a burst as wide as the splash.
                _active.Add(new Effect(EffectKind.Tracer, now, 0.2, new Vector3(shot.From, 0.3f), new Vector3(shot.To, 0.2f), 3.5f, Palette.Flame));
                _active.Add(new Effect(EffectKind.Burst, now, 0.35, new Vector3(shot.To, 0.1f), default, Math.Max(0.4f, shot.Splash), Palette.Flame));
                break;

            case Warhead.Acid:
                // A lazy green glob: slow to fade, no muzzle flash.
                _active.Add(new Effect(EffectKind.Tracer, now, 0.3, new Vector3(shot.From, 0.45f), new Vector3(shot.To, 0.2f), 1.8f, Palette.Acid));
                _active.Add(new Effect(EffectKind.Burst, now, 0.4, new Vector3(shot.To, 0.1f), default, 0.35f, Palette.Acid));
                break;

            default:
                var heavy = shot.Warhead == Warhead.AntiArmour;
                _active.Add(new Effect(EffectKind.Tracer, now, heavy ? 0.16 : 0.09, new Vector3(shot.From, heavy ? 0.42f : 0.35f), new Vector3(shot.To, 0.25f), heavy ? 1.6f : 1f, Palette.Tracer));
                _active.Add(new Effect(EffectKind.Flash, now, heavy ? 0.22 : 0.12, new Vector3(shot.From, 0.4f), default, heavy ? 1.6f : 1f, Palette.Muzzle));
                break;
        }
    }

    public void Death(DeathEvent death, double now) =>
        _active.Add(death.IsCreature
            ? new Effect(EffectKind.Splat, now, 0.7, new Vector3(death.Position, 0.05f), default, 0.35f, Palette.MawFlesh)
            : new Effect(EffectKind.Explosion, now, death.IsStructure ? 0.9 : 0.55, new Vector3(death.Position, 0.2f), default, death.IsStructure ? death.Size * 1.4f : 0.6f, Palette.Explosion));

    public void Marker(Vector2 at, bool attack, double now) =>
        _active.Add(new Effect(attack ? EffectKind.AttackMarker : EffectKind.MoveMarker, now, 0.6, new Vector3(at, 0.02f), default, 1f));

    public void Draw(WorldBatch batch, FrameScene scene, double now)
    {
        _active.RemoveAll(effect => now - effect.Start > effect.Duration);
        foreach (var effect in _active)
        {
            var t = (float)((now - effect.Start) / effect.Duration);
            var fade = 1f - t;
            var depth = effect.From.X + effect.From.Y + 0.5f;
            var lift = batch.GroundAt(effect.From.X, effect.From.Y);
            var raised = effect.From + new Vector3(0, 0, lift);
            switch (effect.Kind)
            {
                case EffectKind.Tracer:
                    batch.Begin(depth + 0.5f);
                    batch.Beam(effect.From, effect.To, 2f * effect.Size, Palette.WithAlpha(effect.Color, fade));
                    batch.End();
                    break;

                case EffectKind.Burst:
                    batch.Begin(depth + 0.6f, lift);
                    batch.Disc(new Vector2(effect.From.X, effect.From.Y), effect.Size * (0.6f + (0.4f * t)), 0.03f, Palette.WithAlpha(effect.Color, 0.7f * fade), emissive: 1f);
                    batch.End();
                    scene.AddLight(new PointLight(raised + new Vector3(0, 0, 0.4f), 1.5f + effect.Size, effect.Color, 0.8f * fade));
                    break;

                case EffectKind.Splat:
                    // No light: a swarm dies by the hundred and must not light up the battlefield.
                    batch.Begin(depth, lift);
                    batch.Disc(new Vector2(effect.From.X, effect.From.Y), effect.Size, 0.015f, Palette.WithAlpha(effect.Color, 0.8f * fade));
                    batch.Billboard(effect.From + new Vector3(0, 0, 0.15f), 6f * fade, Palette.WithAlpha(Palette.MawGlow, fade), emissive: 1f);
                    batch.End();
                    break;

                case EffectKind.Flash:
                    batch.Begin(depth, lift);
                    batch.Billboard(effect.From, 7f * effect.Size * (0.5f + fade), Palette.WithAlpha(Palette.CindriteHot, fade));
                    batch.End();
                    scene.AddLight(new PointLight(raised + new Vector3(0, 0, 0.3f), 2.5f * effect.Size, Palette.Muzzle, 0.9f * fade));
                    break;

                case EffectKind.Explosion:
                    // Scorch on the ground, a fireball that swells and cools, then a puff of smoke.
                    var pixels = 18f * effect.Size;
                    batch.Begin(depth + effect.Size, lift);
                    batch.Disc(new Vector2(effect.From.X, effect.From.Y), effect.Size * 0.8f, 0.015f, Palette.WithAlpha(new LinearColor(0.03f, 0.025f, 0.02f), 0.55f * fade));
                    batch.Billboard(effect.From + new Vector3(0, 0, 0.2f + (t * 0.6f)), pixels * (0.6f + t), Palette.WithAlpha(new LinearColor(0.08f, 0.07f, 0.06f), 0.5f * fade), emissive: 0f);
                    batch.Billboard(effect.From + new Vector3(0, 0, 0.25f), pixels * (0.5f + (t * 0.5f)), Palette.WithAlpha(Palette.Mix(Palette.CindriteHot, Palette.Explosion, t), fade));
                    batch.End();
                    scene.AddLight(new PointLight(raised + new Vector3(0, 0, 0.8f), 3f + (effect.Size * 3f), Palette.Explosion, 1.4f * fade));
                    break;

                case EffectKind.MoveMarker:
                case EffectKind.AttackMarker:
                    var color = effect.Kind == EffectKind.MoveMarker ? Palette.Selection : Palette.HealthLow;
                    batch.Begin(depth - 1f, lift);
                    batch.Disc(new Vector2(effect.From.X, effect.From.Y), 0.5f * (1f - (t * 0.6f)), 0.02f, Palette.WithAlpha(color, fade), emissive: 1f, shape: QuadShape.Ring);
                    batch.End();
                    break;
            }
        }
    }

    private enum EffectKind
    {
        Tracer,
        Burst,
        Splat,
        Flash,
        Explosion,
        MoveMarker,
        AttackMarker,
    }

    private readonly record struct Effect(EffectKind Kind, double Start, double Duration, Vector3 From, Vector3 To, float Size, LinearColor Color = default);
}
