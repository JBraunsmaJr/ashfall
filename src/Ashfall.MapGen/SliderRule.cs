namespace Ashfall.MapGen;

/// <summary>How an archetype reads one slider: the default, and the 0–100 range it allows.</summary>
public readonly record struct SliderRule(int Default, int Min = 0, int Max = 100)
{
    public int Clamp(int value) => value < Min ? Min : value > Max ? Max : value;
}
