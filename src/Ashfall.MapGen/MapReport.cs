namespace Ashfall.MapGen;

/// <summary>
/// The validator's verdict on a generated map: hard failures, and the fairness measurements behind
/// the score. Ratios are in permille (1000 = every start identical).
/// </summary>
public sealed record MapReport
{
    public required IReadOnlyList<string> Failures { get; init; }

    public bool Valid => Failures.Count == 0;

    /// <summary>0–100: 100 when every start has identical paths, resources and space.</summary>
    public required int FairnessScore { get; init; }

    /// <summary>Path length (cells) from each start to its nearest enemy start.</summary>
    public required IReadOnlyList<int> PathToNearestEnemy { get; init; }

    /// <summary>Cindrite (credits) within easy reach of each start.</summary>
    public required IReadOnlyList<long> HomeResources { get; init; }

    /// <summary>Buildable cells around each start.</summary>
    public required IReadOnlyList<int> BuildableArea { get; init; }

    /// <summary>Largest over smallest nearest-enemy path, in permille.</summary>
    public required int PathRatio { get; init; }

    /// <summary>Largest over smallest home resources, in permille.</summary>
    public required int ResourceRatio { get; init; }

    /// <summary>Share of the map's Cindrite that ground units can reach, in permille.</summary>
    public required int ReachableResources { get; init; }

    public long TotalResources { get; init; }
}
