using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Commands;

/// <summary>
/// One player order for one unit. The only input the simulation accepts: humans, AI and remote peers
/// all produce these, and the lockstep layer delivers the same list to every peer for each tick.
/// </summary>
public readonly record struct Command(int PlayerId, CommandKind Kind, Entity Unit, FixVec2 Target)
{
    public static Command Move(int playerId, Entity unit, FixVec2 target) => new(playerId, CommandKind.Move, unit, target);

    public static Command Stop(int playerId, Entity unit) => new(playerId, CommandKind.Stop, unit, FixVec2.Zero);
}
