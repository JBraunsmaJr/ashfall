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
}
