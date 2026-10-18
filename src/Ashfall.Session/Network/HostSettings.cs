using Ashfall.MapGen;
using Ashfall.Networking.Protocol;

namespace Ashfall.Session.Network;

/// <summary>What the host chooses before anyone joins: the seed, the map and the slot layout (the host takes slot 0).</summary>
public sealed record HostSettings
{
    public ulong Seed { get; init; } = ProvingGround.DefaultSeed;

    /// <summary>Slot kinds after the host's own slot. Default: one open seat (a 1v1).</summary>
    public IReadOnlyList<SlotKind> OtherSlots { get; init; } = [SlotKind.Open];

    /// <summary>Team per slot including the host's; default: everyone on their own team.</summary>
    public IReadOnlyList<int>? Teams { get; init; }

    /// <summary>Fixed input delay in ticks; null measures the lobby's round trips and picks 2–6.</summary>
    public int? InputDelay { get; init; }

    /// <summary>The generated map to play, or null for the hand-made duel/quad map.</summary>
    public MapSettings? Map { get; init; }

    public static HostSettings For(int humans, int ais, ulong seed, MapSettings? map = null) => new()
    {
        Seed = seed,
        Map = map,
        OtherSlots = [.. Enumerable.Repeat(SlotKind.Open, humans - 1), .. Enumerable.Repeat(SlotKind.Ai, ais)],
    };
}
