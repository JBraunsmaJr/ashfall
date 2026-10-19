using Ashfall.Rendering;

namespace Ashfall.UI;

/// <summary>
/// Placeholder art direction until cooked sprites arrive, after the concept art: earthy brown ground broken by moss
/// and dark slate rock, acid-green Cindrite that lights the ground around it, gunmetal bases carrying strong faction
/// colours on panels and banners, and the Maw in crimson carapace over violet creep.
/// </summary>
public static class Palette
{
    public static readonly LinearColor Clear = LinearColor.FromSrgb(6, 6, 8);
    public static readonly LinearColor Ground = LinearColor.FromSrgb(112, 84, 56);
    public static readonly LinearColor GroundDark = LinearColor.FromSrgb(80, 60, 42);
    public static readonly LinearColor Moss = LinearColor.FromSrgb(72, 84, 38);
    public static readonly LinearColor MossLight = LinearColor.FromSrgb(98, 112, 50);
    public static readonly LinearColor Grass = LinearColor.FromSrgb(66, 92, 34);
    public static readonly LinearColor Pebble = LinearColor.FromSrgb(92, 88, 84);
    public static readonly LinearColor Rock = LinearColor.FromSrgb(52, 50, 52);
    public static readonly LinearColor RockTop = LinearColor.FromSrgb(78, 75, 72);
    public static readonly LinearColor RockDark = LinearColor.FromSrgb(34, 32, 34);
    public static readonly LinearColor Water = LinearColor.FromSrgb(16, 48, 46);
    public static readonly LinearColor WaterLight = LinearColor.FromSrgb(38, 104, 86);
    public static readonly LinearColor Bridge = LinearColor.FromSrgb(66, 60, 56);
    public static readonly LinearColor BridgeTop = LinearColor.FromSrgb(96, 88, 80);
    public static readonly LinearColor Ramp = LinearColor.FromSrgb(118, 92, 62);
    public static readonly LinearColor Trunk = LinearColor.FromSrgb(58, 42, 30);
    public static readonly LinearColor Leaves = LinearColor.FromSrgb(30, 56, 30);
    public static readonly LinearColor LeavesTop = LinearColor.FromSrgb(46, 78, 38);
    public static readonly LinearColor CindriteGround = LinearColor.FromSrgb(58, 70, 32);
    public static readonly LinearColor Cindrite = LinearColor.FromSrgb(110, 250, 60);
    public static readonly LinearColor CindriteHot = LinearColor.FromSrgb(196, 255, 130);
    public static readonly LinearColor CindriteDeep = LinearColor.FromSrgb(36, 120, 30);
    public static readonly LinearColor Fissure = LinearColor.FromSrgb(34, 38, 24);
    public static readonly LinearColor FissureDepth = LinearColor.FromSrgb(8, 12, 6);

    // Bases: dark gunmetal hulls on paved plates; the faction colour goes on panels, stripes and banners.
    public static readonly LinearColor Concrete = LinearColor.FromSrgb(84, 86, 92);
    public static readonly LinearColor ConcreteDark = LinearColor.FromSrgb(52, 54, 60);
    public static readonly LinearColor Metal = LinearColor.FromSrgb(42, 44, 50);
    public static readonly LinearColor Pavement = LinearColor.FromSrgb(74, 70, 64);
    public static readonly LinearColor PavementSeam = LinearColor.FromSrgb(46, 44, 40);
    public static readonly LinearColor Hazard = LinearColor.FromSrgb(200, 150, 30);
    public static readonly LinearColor HarvesterYellow = LinearColor.FromSrgb(204, 156, 40);
    public static readonly LinearColor Shadow = new(0f, 0f, 0f, 0.42f);
    public static readonly LinearColor Selection = LinearColor.FromSrgb(110, 240, 110);
    public static readonly LinearColor HealthGood = LinearColor.FromSrgb(80, 220, 80);
    public static readonly LinearColor HealthMid = LinearColor.FromSrgb(240, 200, 50);
    public static readonly LinearColor HealthLow = LinearColor.FromSrgb(230, 60, 40);
    public static readonly LinearColor Tracer = LinearColor.FromSrgb(255, 230, 150);
    public static readonly LinearColor Muzzle = LinearColor.FromSrgb(255, 170, 70);
    public static readonly LinearColor Explosion = LinearColor.FromSrgb(255, 120, 30);
    public static readonly LinearColor BaseLight = LinearColor.FromSrgb(255, 196, 130);
    public static readonly LinearColor PowerGlow = LinearColor.FromSrgb(130, 210, 255);

    // The Maw (GDD section 3): crimson carapace and ember eyes over violet creep. Reserved: no faction colour comes
    // near it, so the swarm reads as the swarm at a glance.
    public static readonly LinearColor MawCarapace = LinearColor.FromSrgb(124, 30, 30);
    public static readonly LinearColor MawCarapaceDark = LinearColor.FromSrgb(44, 14, 18);
    public static readonly LinearColor MawFlesh = LinearColor.FromSrgb(160, 46, 40);
    public static readonly LinearColor MawGlow = LinearColor.FromSrgb(255, 72, 34);
    public static readonly LinearColor MawViolet = LinearColor.FromSrgb(150, 64, 210);
    public static readonly LinearColor Creep = LinearColor.FromSrgb(56, 22, 60);
    public static readonly LinearColor CreepVein = LinearColor.FromSrgb(110, 36, 120);
    public static readonly LinearColor Acid = LinearColor.FromSrgb(150, 226, 60);
    public static readonly LinearColor Flame = LinearColor.FromSrgb(255, 150, 40);
    public static readonly LinearColor Wall = LinearColor.FromSrgb(76, 76, 80);

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
        LinearColor.FromSrgb(40, 100, 224),  // Humanity blue
        LinearColor.FromSrgb(212, 40, 34),   // Coalition red
        LinearColor.FromSrgb(232, 166, 30),  // Frontier gold
        LinearColor.FromSrgb(70, 180, 80),
        LinearColor.FromSrgb(236, 112, 170), // not violet: that is the Maw's
        LinearColor.FromSrgb(40, 190, 190),
        LinearColor.FromSrgb(230, 130, 40),
        LinearColor.FromSrgb(200, 200, 200),
    ];

    /// <summary>The colour that marks <paramref name="player"/>'s units and structures (violet for the Maw).</summary>
    public static LinearColor Player(int player) => player >= 0 ? Teams[player % Teams.Length]
        : player == Simulation.GameSimulation.MawPlayerId ? MawViolet : Concrete;

    public static LinearColor Scale(LinearColor color, float factor) =>
        new(color.R * factor, color.G * factor, color.B * factor, color.A);

    public static LinearColor WithAlpha(LinearColor color, float alpha) => color with { A = alpha };

    public static LinearColor Mix(LinearColor a, LinearColor b, float t) =>
        new(a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t), a.A + ((b.A - a.A) * t));
}
