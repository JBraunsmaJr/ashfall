namespace Ashfall.UI.Hud;

/// <summary>One build cameo: a structure to place or a unit to train, with its current state.</summary>
/// <param name="Id">The content id, which picks the cameo's icon.</param>
/// <param name="Label">The short name printed on the cameo.</param>
/// <param name="Name">The full name, shown when the cameo is hovered.</param>
public sealed record SidebarEntry(bool IsStructure, int TypeIndex, string Id, string Label, string Name, int Cost, SidebarTab Tab)
{
    /// <summary>Prerequisites met and a producer (or construction yard) is available.</summary>
    public bool Available { get; set; }

    public bool Affordable { get; set; }

    /// <summary>0..1 while this item is being built (structure on site, unit at the head of a queue); otherwise null.</summary>
    public float? Progress { get; set; }

    /// <summary>Units of this type queued across all producers.</summary>
    public int Queued { get; set; }

    /// <summary>True while this structure is the one being placed.</summary>
    public bool Placing { get; set; }

    /// <summary>Why the cameo is unavailable, for the tooltip line.</summary>
    public string? Reason { get; set; }
}
