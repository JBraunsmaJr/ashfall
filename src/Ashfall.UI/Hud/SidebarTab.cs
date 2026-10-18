namespace Ashfall.UI.Hud;

/// <summary>The sidebar's build tabs (GDD section 5): what each holds is set by role, not by a hand-kept list.</summary>
public enum SidebarTab
{
    /// <summary>Structures that run the base: power, refinery, barracks, factory.</summary>
    Base,

    /// <summary>Walls and, later, turrets.</summary>
    Defence,

    /// <summary>Units trained at the barracks.</summary>
    Infantry,

    /// <summary>Units built at the war factory.</summary>
    Vehicles,
}
