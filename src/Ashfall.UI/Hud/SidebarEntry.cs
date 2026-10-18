namespace Ashfall.UI.Hud;

/// <summary>One build button: a structure to place or a unit to train, with its current state.</summary>
public sealed record SidebarEntry(bool IsStructure, int TypeIndex, string Label, int Cost)
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

    /// <summary>Why the button is unavailable, for the tooltip line.</summary>
    public string? Reason { get; set; }
}
