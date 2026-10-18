using Ashfall.Simulation.Map;

namespace Ashfall.Session.Maps;

/// <summary>The hand-made maps by player count: the duel map for two, the quad map for three or four. Generated maps come from Ashfall.MapGen.</summary>
public static class SkirmishMaps
{
    public const int MaxPlayers = QuadMap.MaxPlayers;

    public static int SizeFor(int players) => players <= 2 ? DuelMap.Size : QuadMap.Size;

    public static GridMap Create(int players) => players <= 2 ? DuelMap.Create() : QuadMap.Create();

    public static Cell StartOrigin(int players, int player) =>
        players <= 2 ? DuelMap.StartOrigin(player) : QuadMap.StartOrigin(player);

    public static string NameFor(int players) => players <= 2 ? "Duel" : "Quad";
}
