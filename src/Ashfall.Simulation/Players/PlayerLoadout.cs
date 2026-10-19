﻿namespace Ashfall.Simulation.Players;

/// <summary>
/// What one player brings into a co-op match from the campaign (GDD section 7): lasting bonuses from research and
/// their commander's perks, extra starting credits and harvesters, and the veteran detachment that lands beside their
/// Construction Yard.
/// </summary>
/// <param name="Modifiers">Research and perk bonuses.</param>
/// <param name="Credits">Starting credits on top of the usual.</param>
/// <param name="Harvesters">Extra harvesters at the start.</param>
/// <param name="Units">Unit ids of the detachment, in order.</param>
public sealed record PlayerLoadout(PlayerModifiers Modifiers, int Credits, int Harvesters, IReadOnlyList<string> Units)
{
    /// <summary>Most units a detachment may hold.</summary>
    public const int MaxUnits = 10;

    public static PlayerLoadout None { get; } = new(PlayerModifiers.None, 0, 0, []);

    public bool Equals(PlayerLoadout? other) =>
        other is not null && other.Modifiers == Modifiers && other.Credits == Credits && other.Harvesters == Harvesters && other.Units.SequenceEqual(Units);

    public override int GetHashCode() => HashCode.Combine(Modifiers, Credits, Harvesters, Units.Count);

    /// <summary>Throws if a match could not start with it.</summary>
    public void Validate()
    {
        Modifiers.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(Credits, nameof(Credits));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Credits, 100_000, nameof(Credits));
        ArgumentOutOfRangeException.ThrowIfNegative(Harvesters, nameof(Harvesters));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Harvesters, 4, nameof(Harvesters));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Units.Count, MaxUnits, nameof(Units));
    }
}
