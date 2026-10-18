using System.Numerics;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.UI.Drawing;

namespace Ashfall.UI.World;

/// <summary>Short-lived visual effects: tracers, muzzle flashes, explosions and order markers, with their lights.</summary>
internal sealed class Effects
{
    private readonly List<Effect> _active = [];

    public int Count => _active.Count;

    public void Shot(ShotEvent shot, bool heavy, double now)
    {
        _active.Add(new Effect(EffectKind.Tracer, now, heavy ? 0.16 : 0.09, new Vector3(shot.From, heavy ? 0.42f : 0.35f), new Vector3(shot.To, 0.25f), heavy ? 1.6f : 1f));
        _active.Add(new Effect(EffectKind.Flash, now, heavy ? 0.22 : 0.12, new Vector3(shot.From, 0.4f), default, heavy ? 1.6f : 1f));
    }

    public void Death(DeathEvent death, double now) =>
        _active.Add(new Effect(EffectKind.Explosion, now, death.IsStructure ? 0.9 : 0.55, new Vector3(death.Position, 0.2f), default, death.IsStructure ? death.Size * 1.4f : 0.6f));

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
            switch (effect.Kind)
            {
                case EffectKind.Tracer:
                    batch.Begin(depth + 0.5f);
                    batch.Beam(effect.From, effect.To, 2f * effect.Size, Palette.WithAlpha(Palette.Tracer, fade));
                    batch.End();
                    break;

                case EffectKind.Flash:
                    batch.Begin(depth);
                    batch.Billboard(effect.From, 7f * effect.Size * (0.5f + fade), Palette.WithAlpha(Palette.CindriteHot, fade));
                    batch.End();
                    scene.AddLight(new PointLight(effect.From + new Vector3(0, 0, 0.3f), 2.5f * effect.Size, Palette.Muzzle, 0.9f * fade));
                    break;

                case EffectKind.Explosion:
                    // Scorch on the ground, a fireball that swells and cools, then a puff of smoke.
                    var pixels = 18f * effect.Size;
                    batch.Begin(depth + effect.Size);
                    batch.Disc(new Vector2(effect.From.X, effect.From.Y), effect.Size * 0.8f, 0.015f, Palette.WithAlpha(new LinearColor(0.03f, 0.025f, 0.02f), 0.55f * fade));
                    batch.Billboard(effect.From + new Vector3(0, 0, 0.2f + (t * 0.6f)), pixels * (0.6f + t), Palette.WithAlpha(new LinearColor(0.08f, 0.07f, 0.06f), 0.5f * fade), emissive: 0f);
                    batch.Billboard(effect.From + new Vector3(0, 0, 0.25f), pixels * (0.5f + (t * 0.5f)), Palette.WithAlpha(Palette.Mix(Palette.CindriteHot, Palette.Explosion, t), fade));
                    batch.End();
                    scene.AddLight(new PointLight(effect.From + new Vector3(0, 0, 0.8f), 3f + (effect.Size * 3f), Palette.Explosion, 1.4f * fade));
                    break;

                case EffectKind.MoveMarker:
                case EffectKind.AttackMarker:
                    var color = effect.Kind == EffectKind.MoveMarker ? Palette.Selection : Palette.HealthLow;
                    batch.Begin(depth - 1f);
                    batch.Disc(new Vector2(effect.From.X, effect.From.Y), 0.5f * (1f - (t * 0.6f)), 0.02f, Palette.WithAlpha(color, fade), emissive: 1f, shape: QuadShape.Ring);
                    batch.End();
                    break;
            }
        }
    }

    private enum EffectKind
    {
        Tracer,
        Flash,
        Explosion,
        MoveMarker,
        AttackMarker,
    }

    private readonly record struct Effect(EffectKind Kind, double Start, double Duration, Vector3 From, Vector3 To, float Size);
}
