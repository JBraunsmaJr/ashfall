namespace Ashfall.Content;

/// <summary>How a target resists damage. Values are part of the replay format: append only.</summary>
public enum ArmorType : byte
{
    /// <summary>Infantry.</summary>
    None = 0,
    Light = 1,
    Heavy = 2,
    Structure = 3,
    Air = 4,
}
