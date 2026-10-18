namespace Ashfall.Session.Network;

public enum SessionState
{
    /// <summary>Opening the connection to the host (or, when rejoining, to a player in the match).</summary>
    Connecting,

    /// <summary>In the lobby: choosing teams, getting ready, waiting for the host to start.</summary>
    Lobby,

    /// <summary>The match is running (or stalled waiting for a turn).</summary>
    Playing,

    /// <summary>Peers disagreed about the game state; the match is halted and dumps were written.</summary>
    Desynced,

    /// <summary>The match finished, or this player left it.</summary>
    Ended,

    /// <summary>Could not join, or lost the host while in the lobby. See <see cref="NetworkSession.Error"/>.</summary>
    Failed,
}
