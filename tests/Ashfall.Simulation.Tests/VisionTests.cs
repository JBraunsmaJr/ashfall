using Ashfall.Content;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using Ashfall.Simulation.Systems;
using Ashfall.Simulation.Vision;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

public class VisionTests
{
    [Fact]
    public void Units_reveal_a_disc_of_their_sight_range_to_their_owner_only()
    {
        var sim = Create();
        sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(10, 10).Center);

        sim.Step([]);

        var vision = sim.Visibility;
        Assert.True(vision.IsVisible(0, new Cell(10, 10)));
        Assert.True(vision.IsVisible(0, new Cell(16, 10)));  // sight 6
        Assert.True(vision.IsVisible(0, new Cell(14, 14)));  // inside the rounded disc
        Assert.False(vision.IsVisible(0, new Cell(15, 15))); // outside it
        Assert.False(vision.IsVisible(0, new Cell(17, 10)));
        Assert.Equal(CellVisibility.Shroud, vision.At(1, new Cell(10, 10)));
    }

    [Fact]
    public void Cells_left_behind_turn_to_fog_and_unseen_cells_stay_shrouded()
    {
        var sim = Create();
        var unit = sim.SpawnUnit(0, Unit(BulwarkContent.Warden), new Cell(3, 3).Center);

        sim.Step([Command.Move(0, unit, new Cell(28, 3).Center)]);
        RunUntil(sim, s => PositionOf(s, unit) == new Cell(28, 3).Center, maxTicks: 1_000);
        Run(sim, VisionSystem.Period);

        Assert.Equal(CellVisibility.Fog, sim.Visibility.At(0, new Cell(3, 3)));
        Assert.Equal(CellVisibility.Visible, sim.Visibility.At(0, new Cell(28, 3)));
        Assert.Equal(CellVisibility.Shroud, sim.Visibility.At(0, new Cell(3, 25)));
    }

    [Fact]
    public void Teammates_share_vision()
    {
        var sim = new GameSimulation(1, new GridMap(32, 32), GameContent.Standard,
            [new PlayerSetup(0, BulwarkContent.FactionId), new PlayerSetup(0, BulwarkContent.FactionId), new PlayerSetup(1, BulwarkContent.FactionId)],
            victoryEnabled: false);
        sim.PlaceStructure(1, Structure(BulwarkContent.PowerPlant), new Cell(20, 20));

        sim.Step([]);

        Assert.True(sim.Visibility.IsVisible(0, new Cell(21, 21)));
        Assert.True(sim.Visibility.IsVisible(1, new Cell(21, 21)));
        Assert.False(sim.Visibility.IsExplored(2, new Cell(21, 21)));
    }

    [Fact]
    public void Destroyed_units_stop_revealing()
    {
        var sim = Create();
        var unit = sim.SpawnUnit(0, Unit(BulwarkContent.Rifleman), new Cell(10, 10).Center);
        sim.Step([]);

        sim.World.Get<Components.Health>(unit) = sim.World.Get<Components.Health>(unit) with { Current = 0 };
        Run(sim, VisionSystem.Period);

        Assert.Equal(CellVisibility.Fog, sim.Visibility.At(0, new Cell(10, 10)));
    }

    [Fact]
    public void Vision_is_part_of_the_state_hash()
    {
        var a = new VisibilityMap(8, 8, 2);
        var b = new VisibilityMap(8, 8, 2);
        Assert.Equal(Hash(a), Hash(b));

        b.Reveal(1, new Cell(7, 7), 0);
        Assert.NotEqual(Hash(a), Hash(b));
    }

    private static ulong Hash(VisibilityMap map)
    {
        var hasher = default(Core.StateHasher);
        map.HashInto(ref hasher);
        return hasher.Value;
    }
}
