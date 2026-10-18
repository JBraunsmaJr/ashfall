using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Navigation;

namespace Ashfall.Simulation.Tests;

public class NavigationTests
{
    [Fact]
    public void Open_ground_gives_the_octile_shortest_path()
    {
        var finder = new GridPathfinder(new GridMap(16, 16));
        var path = new List<Cell>();

        Assert.True(finder.FindPath(new Cell(0, 0), new Cell(5, 3), path));

        Assert.Equal(5, path.Count); // 3 diagonal + 2 straight steps; the start is not included
        Assert.Equal(new Cell(5, 3), path[^1]);
        AssertConnected(new Cell(0, 0), path);
    }

    [Fact]
    public void Paths_go_around_walls_without_cutting_corners()
    {
        var map = new GridMap(10, 10);
        for (var y = 0; y < 9; y++)
        {
            map.SetTerrain(new Cell(5, y), Terrain.Rock);
        }

        var path = new List<Cell>();
        Assert.True(new GridPathfinder(map).FindPath(new Cell(2, 2), new Cell(8, 2), path));

        Assert.Contains(new Cell(5, 9), path);
        Assert.All(path, cell => Assert.True(map.IsPassable(cell)));
        AssertConnected(new Cell(2, 2), path);
        for (var i = 1; i < path.Count; i++)
        {
            var (a, b) = (path[i - 1], path[i]);
            if (a.X != b.X && a.Y != b.Y)
            {
                Assert.True(map.IsPassable(new Cell(b.X, a.Y)) && map.IsPassable(new Cell(a.X, b.Y)), $"Corner cut between {a} and {b}.");
            }
        }
    }

    [Fact]
    public void An_unreachable_goal_gives_a_partial_path_to_the_closest_reachable_cell()
    {
        var map = new GridMap(10, 10);
        for (var y = 0; y < 10; y++)
        {
            map.SetTerrain(new Cell(5, y), Terrain.Rock);
        }

        var path = new List<Cell>();
        Assert.False(new GridPathfinder(map).FindPath(new Cell(1, 4), new Cell(8, 4), path));

        Assert.Equal(new Cell(4, 4), path[^1]);
    }

    [Fact]
    public void The_same_search_always_returns_the_same_path()
    {
        var map = new GridMap(20, 20);
        map.SetStructure(new Cell(8, 8), 4, 4, entityIndex: 1);
        var first = new List<Cell>();
        var second = new List<Cell>();

        new GridPathfinder(map).FindPath(new Cell(2, 10), new Cell(17, 10), first);
        var reused = new GridPathfinder(map);
        reused.FindPath(new Cell(0, 0), new Cell(19, 19), second);
        reused.FindPath(new Cell(2, 10), new Cell(17, 10), second);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_clear_line_is_a_single_waypoint()
    {
        var navigator = new Navigator(new GridMap(16, 16));
        var mover = Mover.Idle(Fix64.Half);

        Assert.True(navigator.PlanRoute(0, FixVec2.FromInts(1, 1), FixVec2.FromInts(10, 7), ref mover));

        Assert.Equal(1, navigator.Paths.CountFor(0));
        Assert.Equal(FixVec2.FromInts(10, 7), mover.Target);
        Assert.True(mover.HasTarget);
    }

    [Fact]
    public void Routes_around_obstacles_are_smoothed_and_stay_clear()
    {
        var map = new GridMap(16, 16);
        for (var y = 0; y < 12; y++)
        {
            map.SetTerrain(new Cell(8, y), Terrain.Rock);
        }

        var navigator = new Navigator(map);
        var mover = Mover.Idle(Fix64.Half);
        var from = new Cell(3, 3).Center;

        Assert.True(navigator.PlanRoute(0, from, new Cell(13, 3).Center, ref mover));

        var waypoints = navigator.Paths.For(0);
        Assert.InRange(waypoints.Count, 2, 4); // a couple of legs around the wall's end, not one per cell
        Assert.Equal(new Cell(13, 3).Center, waypoints[^1]);
        var previous = from;
        foreach (var waypoint in waypoints)
        {
            Assert.True(navigator.HasClearLine(previous, waypoint), $"Leg {previous} -> {waypoint} crosses rock.");
            previous = waypoint;
        }
    }

    [Fact]
    public void A_blocked_destination_is_replaced_by_the_nearest_free_cell()
    {
        var map = new GridMap(16, 16);
        map.SetStructure(new Cell(6, 6), 3, 3, entityIndex: 1);
        var navigator = new Navigator(map);
        var mover = Mover.Idle(Fix64.Half);

        Assert.True(navigator.PlanRoute(0, FixVec2.FromInts(1, 1), new Cell(7, 7).Center, ref mover));

        var end = Cell.Containing(mover.Target);
        Assert.True(map.IsPassable(end));
        Assert.Equal(2, Cell.ChebyshevDistance(end, new Cell(7, 7)));
    }

    [Fact]
    public void Units_follow_routes_around_obstacles_in_the_simulation()
    {
        var map = new GridMap(16, 16);
        for (var y = 0; y < 12; y++)
        {
            map.SetTerrain(new Cell(8, y), Terrain.Rock);
        }

        var sim = TestMatch.Create(map: map);
        var unit = sim.SpawnUnit(0, TestMatch.Unit(Ashfall.Content.BulwarkContent.Rifleman), new Cell(3, 3).Center);
        var target = new Cell(13, 3).Center;

        sim.Step([Commands.Command.Move(0, unit, target)]);
        TestMatch.RunUntil(sim, s =>
        {
            Assert.True(map.IsPassable(Cell.Containing(TestMatch.PositionOf(s, unit))));
            return TestMatch.PositionOf(s, unit) == target;
        }, maxTicks: 2_000);
    }

    private static void AssertConnected(Cell start, List<Cell> path)
    {
        var previous = start;
        foreach (var cell in path)
        {
            Assert.Equal(1, Cell.ChebyshevDistance(previous, cell));
            previous = cell;
        }
    }
}
