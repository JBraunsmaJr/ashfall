namespace Ashfall.Session;

/// <summary>Who issues a skirmish player's commands.</summary>
public enum PlayerController
{
    /// <summary>The M1 scripted AI: builds a base, trains an army and attacks.</summary>
    ScriptedAi,

    /// <summary>A dummy opponent that never issues a command: a sandbox for learning the controls.</summary>
    IdleAi,

    /// <summary>A person, through the client's <see cref="Commands.LocalOrders"/>.</summary>
    Human,
}
