using System.Globalization;
using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Horde;
using Ashfall.UI.World;

namespace Ashfall.UI.Hud;

/// <summary>
/// What the interface says about a wave the Maw's director has called (GDD section 3: every wave is telegraphed):
/// what it is, whom it is for, and which way it will come from. Compass directions are the minimap's: north is up.
/// </summary>
public static class MawTelegraph
{
    /// <summary>The announcement when a wave is called, from <paramref name="viewer"/>'s point of view (-1 for a spectator).</summary>
    public static string Called(GameSimulation simulation, WaveOrder order, int viewer)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var from = From(simulation, order, viewer);
        var whom = Whom(order.Target, viewer);
        var text = order.Kind switch
        {
            WaveKind.Assault when order.SecondTarget >= 0 && (order.Target == viewer || order.SecondTarget == viewer) => "THE MAW STIRS - ASSAULT ON YOU AND AN ALLY",
            WaveKind.Assault when order.SecondTarget >= 0 => "THE MAW STIRS - SPLIT ASSAULT",
            WaveKind.Assault => "THE MAW STIRS - ASSAULT ON " + whom,
            WaveKind.Raid => "RAID ON " + (order.Target == viewer ? "YOUR" : whom + "'S") + " HARVESTERS",
            _ => "MAW PROBE AT " + whom,
        };
        return from is null ? text : text + " FROM THE " + from;
    }

    /// <summary>The announcement when the wave goes.</summary>
    public static string Launched(WaveKind kind) => kind switch
    {
        WaveKind.Assault => "ASSAULT INCOMING",
        WaveKind.Raid => "RAID INCOMING",
        _ => "PROBE INCOMING",
    };

    /// <summary>The short form for the Maw line under the clock, with the countdown.</summary>
    public static string Countdown(GameSimulation simulation, WaveOrder order, int viewer)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var until = TimeSpan.FromSeconds(Math.Max(0, order.LaunchTick - simulation.Tick) / (double)SimulationConstants.TicksPerSecond);
        var kind = order.Kind.ToString().ToUpperInvariant();
        var from = From(simulation, order, viewer);
        return string.Create(CultureInfo.InvariantCulture, $"{kind} IN {(int)until.TotalMinutes:00}:{until.Seconds:00}{(from is null ? string.Empty : " - " + from)}");
    }

    /// <summary>The middle of a player's base: its Construction Yard, else its first structure (walls aside); null if it has none.</summary>
    public static Vector2? BaseOf(GameSimulation simulation, int player)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var world = simulation.World;
        Vector2? any = null;
        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId != player)
            {
                continue;
            }

            var role = simulation.Content.Structure(world.Store<StructureInfo>().Get(index).StructureType).Role;
            var position = MotionTracker.ToVector(world.Store<Position>().Get(index).Value);
            if (role == StructureRole.ConstructionYard)
            {
                return position;
            }

            if (role != StructureRole.Wall)
            {
                any ??= position;
            }
        }

        return any;
    }

    /// <summary>Eight-way compass name of the direction from <paramref name="from"/> to <paramref name="to"/>, north up.</summary>
    public static string Compass(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        if (delta.LengthSquared() < 1e-6f)
        {
            return "NORTH";
        }

        // Screen-style angle: 0 is east, a quarter turn clockwise is south (y grows downward on the minimap).
        var sector = (int)MathF.Round(MathF.Atan2(delta.Y, delta.X) / (MathF.PI / 4f));
        return (((sector % 8) + 8) % 8) switch
        {
            0 => "EAST",
            1 => "SOUTH-EAST",
            2 => "SOUTH",
            3 => "SOUTH-WEST",
            4 => "WEST",
            5 => "NORTH-WEST",
            6 => "NORTH",
            _ => "NORTH-EAST",
        };
    }

    // Which way the wave will come from, seen from the viewer's base when it is coming for the viewer, else from its
    // target's.
    private static string? From(GameSimulation simulation, WaveOrder order, int viewer) =>
        order.Source.X < 0 || BaseOf(simulation, viewer >= 0 && order.SecondTarget == viewer ? viewer : order.Target) is not { } home
            ? null
            : Compass(home, MotionTracker.ToVector(order.Source.Center));

    private static string Whom(int target, int viewer) =>
        target == viewer ? "YOU" : viewer >= 0 ? "AN ALLY" : "PLAYER " + (target + 1).ToString(CultureInfo.InvariantCulture);
}
