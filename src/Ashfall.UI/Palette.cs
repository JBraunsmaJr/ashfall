using Ashfall.Rendering;

namespace Ashfall.UI;

/// <summary>Placeholder art direction until cooked sprites arrive: ash ground, ember-orange Cindrite, team colours.</summary>
public static class Palette
{
    public static readonly LinearColor Clear = LinearColor.FromSrgb(8, 8, 10);
    public static readonly LinearColor Ground = LinearColor.FromSrgb(104, 94, 82);
    public static readonly LinearColor GroundDark = LinearColor.FromSrgb(86, 78, 68);
    public static readonly LinearColor Rock = LinearColor.FromSrgb(72, 72, 80);
    public static readonly LinearColor RockTop = LinearColor.FromSrgb(98, 98, 106);
    public static readonly LinearColor Water = LinearColor.FromSrgb(38, 74, 112);
    public static readonly LinearColor WaterLight = LinearColor.FromSrgb(70, 118, 160);
    public static readonly LinearColor Bridge = LinearColor.FromSrgb(122, 96, 66);
    public static readonly LinearColor BridgeTop = LinearColor.FromSrgb(150, 120, 84);
    public static readonly LinearColor Ramp = LinearColor.FromSrgb(124, 112, 92);
    public static readonly LinearColor Trunk = LinearColor.FromSrgb(74, 56, 40);
    public static readonly LinearColor Leaves = LinearColor.FromSrgb(52, 96, 50);
    public static readonly LinearColor LeavesTop = LinearColor.FromSrgb(72, 124, 64);
    public static readonly LinearColor CindriteGround = LinearColor.FromSrgb(96, 64, 44);
    public static readonly LinearColor Cindrite = LinearColor.FromSrgb(255, 128, 36);
    public static readonly LinearColor CindriteHot = LinearColor.FromSrgb(255, 196, 92);
    public static readonly LinearColor Concrete = LinearColor.FromSrgb(156, 154, 146);
    public static readonly LinearColor ConcreteDark = LinearColor.FromSrgb(112, 110, 104);
    public static readonly LinearColor Metal = LinearColor.FromSrgb(84, 88, 94);
    public static readonly LinearColor HarvesterYellow = LinearColor.FromSrgb(214, 170, 52);
    public static readonly LinearColor Shadow = new(0f, 0f, 0f, 0.38f);
    public static readonly LinearColor Selection = LinearColor.FromSrgb(110, 240, 110);
    public static readonly LinearColor HealthGood = LinearColor.FromSrgb(80, 220, 80);
    public static readonly LinearColor HealthMid = LinearColor.FromSrgb(240, 200, 50);
    public static readonly LinearColor HealthLow = LinearColor.FromSrgb(230, 60, 40);
    public static readonly LinearColor Tracer = LinearColor.FromSrgb(255, 230, 150);
    public static readonly LinearColor Muzzle = LinearColor.FromSrgb(255, 170, 70);
    public static readonly LinearColor Explosion = LinearColor.FromSrgb(255, 120, 30);
    public static readonly LinearColor BaseLight = LinearColor.FromSrgb(255, 210, 150);
    public static readonly LinearColor PowerGlow = LinearColor.FromSrgb(140, 220, 255);

    public static readonly LinearColor Panel = LinearColor.FromSrgb(28, 30, 34, 245);
    public static readonly LinearColor PanelLight = LinearColor.FromSrgb(48, 52, 58);
    public static readonly LinearColor PanelEdge = LinearColor.FromSrgb(90, 96, 104);
    public static readonly LinearColor Button = LinearColor.FromSrgb(44, 48, 54);
    public static readonly LinearColor ButtonHover = LinearColor.FromSrgb(62, 68, 76);
    public static readonly LinearColor ButtonActive = LinearColor.FromSrgb(70, 96, 70);
    public static readonly LinearColor Progress = LinearColor.FromSrgb(60, 120, 170, 200);
    public static readonly LinearColor Text = LinearColor.FromSrgb(226, 222, 210);
    public static readonly LinearColor TextDim = LinearColor.FromSrgb(120, 120, 120);
    public static readonly LinearColor TextWarn = LinearColor.FromSrgb(240, 90, 70);
    public static readonly LinearColor TextGood = LinearColor.FromSrgb(140, 230, 120);
    public static readonly LinearColor Credits = LinearColor.FromSrgb(250, 200, 90);
    public static readonly LinearColor PlaceOk = new(0.2f, 0.9f, 0.3f, 0.45f);
    public static readonly LinearColor PlaceBad = new(0.95f, 0.2f, 0.15f, 0.45f);

    private static readonly LinearColor[] Teams =
    [
        LinearColor.FromSrgb(58, 128, 228),
        LinearColor.FromSrgb(214, 58, 46),
        LinearColor.FromSrgb(70, 180, 80),
        LinearColor.FromSrgb(220, 190, 50),
        LinearColor.FromSrgb(150, 80, 200),
        LinearColor.FromSrgb(40, 190, 190),
        LinearColor.FromSrgb(230, 130, 40),
        LinearColor.FromSrgb(200, 200, 200),
    ];

    /// <summary>The colour that marks <paramref name="player"/>'s units and structures.</summary>
    public static LinearColor Player(int player) => player >= 0 ? Teams[player % Teams.Length] : Concrete;

    public static LinearColor Scale(LinearColor color, float factor) =>
        new(color.R * factor, color.G * factor, color.B * factor, color.A);

    public static LinearColor WithAlpha(LinearColor color, float alpha) => color with { A = alpha };

    public static LinearColor Mix(LinearColor a, LinearColor b, float t) =>
        new(a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t), a.A + ((b.A - a.A) * t));
}
