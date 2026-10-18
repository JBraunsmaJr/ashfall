namespace Ashfall.Session.Network;

/// <summary>How a <see cref="NetworkSession"/> runs locally. None of this has to match between peers.</summary>
public sealed record NetworkOptions
{
    /// <summary>Pacing for the match clock (lockstep still waits for every turn).</summary>
    public GameSpeed Speed { get; init; } = GameSpeed.RealTime;

    /// <summary>Clock for pacing and timeouts; tests pass a manual one.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The port other players connect to for the peer mesh (the transport's local port).</summary>
    public int? AdvertisedPort { get; init; }

    /// <summary>Let the scripted AI play this seat (headless bots, soak tests, demos).</summary>
    public bool Bot { get; init; }

    /// <summary>Mark this player ready as soon as the mesh is complete (bots).</summary>
    public bool AutoReady { get; init; }

    /// <summary>Where desync dumps are written; null disables them.</summary>
    public string? DumpDirectory { get; init; } = "desync-dumps";

    /// <summary>Most ticks one <see cref="NetworkSession.Update"/> may run (bounds catch-up bursts).</summary>
    public int MaxTicksPerUpdate { get; init; } = 30;
}
