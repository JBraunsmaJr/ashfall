namespace Ashfall.Core;

/// <summary>The complete state of a <see cref="DeterministicRandom"/>.</summary>
public readonly record struct RandomSnapshot(ulong State, ulong Increment);
