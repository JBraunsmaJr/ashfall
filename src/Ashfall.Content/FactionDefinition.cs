namespace Ashfall.Content;

/// <summary>A playable faction: which units and structures it can build, and what it starts with.</summary>
public sealed record FactionDefinition(
    string Id,
    string Name,
    string StartingStructureId,
    IReadOnlyList<string> StructureIds,
    IReadOnlyList<string> UnitIds);
