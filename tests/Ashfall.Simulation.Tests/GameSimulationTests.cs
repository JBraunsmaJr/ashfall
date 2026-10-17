using Ashfall.Core;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Tests;

public class GameSimulationTests
{
    private static readonly Fix64 HalfCellPerTick = Fix64.Half;

    [Fact]
    public void A_unit_reaches_its_target_exactly_on_the_expected_tick()
    {
        var sim = new GameSimulation(1, new MapBounds(32, 32));
        var unit = sim.SpawnUnit(0, FixVec2.FromInts(1, 1), HalfCellPerTick);

        sim.Step([Command.Move(0, unit, FixVec2.FromInts(6, 1))]);
        for (var tick = 1; tick < 9; tick++)
        {
            sim.Step([]);
        }

        Assert.NotEqual(FixVec2.FromInts(6, 1), PositionOf(sim, unit));

        sim.Step([]);

        Assert.Equal(FixVec2.FromInts(6, 1), PositionOf(sim, unit));
        Assert.False(sim.World.Get<Mover>(unit).HasTarget);
        Assert.Equal(10, sim.Tick);
    }

    [Fact]
    public void A_diagonal_move_ends_exactly_on_the_target()
    {
        var sim = new GameSimulation(1, new MapBounds(32, 32));
        var unit = sim.SpawnUnit(0, FixVec2.FromInts(1, 1), HalfCellPerTick);
        var target = FixVec2.FromInts(4, 5);

        sim.Step([Command.Move(0, unit, target)]);
        for (var tick = 0; tick < 20; tick++)
        {
            sim.Step([]);
        }

        Assert.Equal(target, PositionOf(sim, unit));
    }

    [Fact]
    public void Orders_for_another_players_unit_are_rejected()
    {
        var sim = new GameSimulation(1, new MapBounds(32, 32));
        var unit = sim.SpawnUnit(0, FixVec2.FromInts(1, 1), HalfCellPerTick);

        sim.Step([Command.Move(1, unit, FixVec2.FromInts(9, 9))]);

        Assert.Equal(1, sim.RejectedCommands);
        Assert.False(sim.World.Get<Mover>(unit).HasTarget);
    }

    [Fact]
    public void Orders_for_dead_units_are_rejected()
    {
        var sim = new GameSimulation(1, new MapBounds(32, 32));
        var unit = sim.SpawnUnit(0, FixVec2.FromInts(1, 1), HalfCellPerTick);
        sim.World.Destroy(unit);
        sim.Step([]);

        sim.Step([Command.Move(0, unit, FixVec2.FromInts(9, 9)), Command.Stop(0, Entity.None)]);

        Assert.Equal(2, sim.RejectedCommands);
    }

    [Fact]
    public void Move_targets_are_clamped_to_the_map()
    {
        var sim = new GameSimulation(1, new MapBounds(8, 8));
        var unit = sim.SpawnUnit(0, FixVec2.FromInts(1, 1), HalfCellPerTick);

        sim.Step([Command.Move(0, unit, FixVec2.FromInts(100, -5))]);

        var target = sim.World.Get<Mover>(unit).Target;
        Assert.True(sim.Bounds.Contains(target));
        Assert.Equal(Fix64.Zero, target.Y);
        Assert.Equal(7, target.X.FloorToInt());
    }

    [Fact]
    public void Stop_cancels_movement()
    {
        var sim = new GameSimulation(1, new MapBounds(32, 32));
        var unit = sim.SpawnUnit(0, FixVec2.FromInts(1, 1), HalfCellPerTick);
        sim.Step([Command.Move(0, unit, FixVec2.FromInts(20, 1))]);

        sim.Step([Command.Stop(0, unit)]);
        var stoppedAt = PositionOf(sim, unit);
        sim.Step([]);

        Assert.Equal(stoppedAt, PositionOf(sim, unit));
    }

    [Fact]
    public void Command_order_between_players_does_not_change_the_result()
    {
        var (simA, a0, a1) = TwoPlayerSetup();
        var (simB, b0, b1) = TwoPlayerSetup();

        simA.Step([Command.Move(0, a0, FixVec2.FromInts(5, 5)), Command.Move(1, a1, FixVec2.FromInts(9, 2))]);
        simB.Step([Command.Move(1, b1, FixVec2.FromInts(9, 2)), Command.Move(0, b0, FixVec2.FromInts(5, 5))]);

        Assert.Equal(simA.ComputeStateHash(), simB.ComputeStateHash());
    }

    [Fact]
    public void Within_one_player_the_last_order_wins()
    {
        var sim = new GameSimulation(1, new MapBounds(32, 32));
        var unit = sim.SpawnUnit(0, FixVec2.FromInts(1, 1), HalfCellPerTick);

        sim.Step([Command.Move(0, unit, FixVec2.FromInts(5, 5)), Command.Move(0, unit, FixVec2.FromInts(2, 9))]);

        Assert.Equal(FixVec2.FromInts(2, 9), sim.World.Get<Mover>(unit).Target);
    }

    [Fact]
    public void Identical_inputs_give_identical_hashes_and_any_difference_shows()
    {
        var (simA, a0, _) = TwoPlayerSetup();
        var (simB, b0, _) = TwoPlayerSetup();
        Assert.Equal(simA.ComputeStateHash(), simB.ComputeStateHash());

        simA.Step([Command.Move(0, a0, FixVec2.FromInts(5, 5))]);
        simB.Step([Command.Move(0, b0, FixVec2.FromInts(5, 6))]);

        Assert.NotEqual(simA.ComputeStateHash(), simB.ComputeStateHash());
    }

    [Fact]
    public void A_map_with_no_area_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSimulation(1, new MapBounds(0, 10)));
    }

    private static (GameSimulation Simulation, Entity Player0Unit, Entity Player1Unit) TwoPlayerSetup()
    {
        var sim = new GameSimulation(5, new MapBounds(16, 16));
        var p0 = sim.SpawnUnit(0, FixVec2.FromInts(1, 1), HalfCellPerTick);
        var p1 = sim.SpawnUnit(1, FixVec2.FromInts(14, 14), HalfCellPerTick);
        return (sim, p0, p1);
    }

    private static FixVec2 PositionOf(GameSimulation sim, Entity unit) => sim.World.Get<Position>(unit).Value;
}
