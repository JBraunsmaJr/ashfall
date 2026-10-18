namespace Ashfall.Content;

/// <summary>Match-wide economy numbers. Lobby options will override some of these.</summary>
public sealed record EconomyRules
{
    public int StartingCredits { get; init; } = 5_000;

    /// <summary>Cindrite (in credits) taken per harvest action.</summary>
    public int HarvestAmount { get; init; } = 25;

    /// <summary>Ticks between harvest actions while a harvester sits on a field cell.</summary>
    public int HarvestIntervalTicks { get; init; } = 5;

    /// <summary>Credits transferred per tick while a harvester unloads at a refinery.</summary>
    public int UnloadPerTick { get; init; } = 50;

    /// <summary>Production runs at 1/this speed while power demand exceeds supply.</summary>
    public int LowPowerSlowdown { get; init; } = 2;

    /// <summary>New structures must start within this many cells of one the player already owns.</summary>
    public int BuildRadius { get; init; } = 6;

    /// <summary>Units queued per production structure.</summary>
    public int QueueCapacity { get; init; } = 5;

    /// <summary>
    /// In matches with regrowth: Cindrite a field cell regains every <see cref="CindriteRegrowIntervalTicks"/>, up to
    /// the density it started with. A 25-cell field regains 12.5 a second: less than a fed nest eats, far less
    /// than a harvester takes.
    /// </summary>
    public int CindriteRegrowAmount { get; init; } = 5;

    public int CindriteRegrowIntervalTicks { get; init; } = 150;
}
