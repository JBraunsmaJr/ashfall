using Ashfall.Core;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;

namespace Ashfall.Simulation.Commands;

/// <summary>
/// One player order. The only input the simulation accepts: humans, AI and remote peers all produce
/// these, and the lockstep layer delivers the same list to every peer for each tick. Use the factory
/// methods; which fields matter depends on <see cref="Kind"/>.
/// </summary>
/// <param name="Unit">The unit (or, for <see cref="CommandKind.Train"/>, the production structure) being ordered.</param>
/// <param name="Target">Destination point for movement orders.</param>
/// <param name="TargetEntity">Victim of <see cref="CommandKind.Attack"/>.</param>
/// <param name="TypeIndex">Structure type for Build, unit type for Train.</param>
/// <param name="Cell">Footprint origin for Build.</param>
public readonly record struct Command(
    int PlayerId,
    CommandKind Kind,
    Entity Unit,
    FixVec2 Target,
    Entity TargetEntity,
    int TypeIndex,
    Cell Cell)
{
    public static Command Move(int playerId, Entity unit, FixVec2 target) =>
        new(playerId, CommandKind.Move, unit, target, Entity.None, -1, default);

    public static Command Stop(int playerId, Entity unit) =>
        new(playerId, CommandKind.Stop, unit, FixVec2.Zero, Entity.None, -1, default);

    public static Command Attack(int playerId, Entity unit, Entity target) =>
        new(playerId, CommandKind.Attack, unit, FixVec2.Zero, target, -1, default);

    public static Command AttackMove(int playerId, Entity unit, FixVec2 target) =>
        new(playerId, CommandKind.AttackMove, unit, target, Entity.None, -1, default);

    public static Command Build(int playerId, int structureType, Cell origin) =>
        new(playerId, CommandKind.Build, Entity.None, FixVec2.Zero, Entity.None, structureType, origin);

    public static Command Train(int playerId, Entity producer, int unitType) =>
        new(playerId, CommandKind.Train, producer, FixVec2.Zero, Entity.None, unitType, default);
}
