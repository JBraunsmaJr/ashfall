using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Recomputes every player's fog of war from the sight of their team's units and structures.
/// Runs every <see cref="Period"/> ticks: vision changes slowly and the full restamp is the costly part.
/// </summary>
public sealed class VisionSystem : ISystem
{
    /// <summary>Ticks between vision updates (5 per second at 15 Hz).</summary>
    public const int Period = 3;

    public void Update(in TickContext context)
    {
        var vision = context.Simulation.Visibility;
        if (vision.Players == 0 || context.Tick % Period != 0)
        {
            return;
        }

        vision.BeginUpdate();
        var world = context.World;
        var owners = world.Store<Owner>();
        var positions = world.Store<Position>();

        foreach (var index in world.Store<UnitInfo>().EntityIndices)
        {
            var sight = context.Content.Unit(world.Store<UnitInfo>().Get(index).UnitType).Sight;
            RevealForTeam(context, owners.Get(index).PlayerId, Cell.Containing(positions.Get(index).Value), sight);
        }

        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            var info = world.Store<StructureInfo>().Get(index);
            var definition = context.Content.Structure(info.StructureType);
            var center = new Cell(info.Origin.X + (definition.Width / 2), info.Origin.Y + (definition.Height / 2));
            var reach = definition.Sight + ((definition.Width > definition.Height ? definition.Width : definition.Height) / 2);
            RevealForTeam(context, owners.Get(index).PlayerId, center, reach);
        }
    }

    private static void RevealForTeam(in TickContext context, int owner, Cell center, int radius)
    {
        var players = context.Players;
        if (owner < 0 || owner >= players.Count)
        {
            return;
        }

        var team = players[owner].Team;
        foreach (var player in players)
        {
            if (player.Team == team)
            {
                context.Simulation.Visibility.Reveal(player.Id, center, radius);
            }
        }
    }
}
