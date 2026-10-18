namespace Ashfall.UI.Menu;

/// <summary>What the player picked on the main menu.</summary>
public enum MenuAction
{
    Skirmish,
    Host,
    Join,
    Quit,

    /// <summary>A co-op Purge against the Maw, with AI allies.</summary>
    Purge,
}
