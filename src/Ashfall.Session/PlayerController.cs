namespace Ashfall.Session;

/// <summary>Who issues a skirmish player's commands.</summary>
public enum PlayerController
{
    /// <summary>The M1 scripted AI: builds a base, trains an army and attacks.</summary>
    ScriptedAi,

    /// <summary>A dummy opponent that never issues a command: a sandbox for learning the controls.</summary>
    IdleAi,

    /// <summary>A person at this machine, through the client's <see cref="Commands.LocalOrders"/>.</summary>
    Human,

    /// <summary>
    /// A seat whose commands arrive as lockstep turns: every person in a network match (this machine's
    /// player included, since its own turns are scheduled the same way) and every seat in a replay.
    /// </summary>
    Remote,
}
