using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

public class WallPlacementTests
{
    [Fact]
    public void A_wall_line_follows_the_longer_axis_of_the_drag()
    {
        Assert.Equal([new Cell(5, 5), new Cell(6, 5), new Cell(7, 5), new Cell(8, 5)], GameScreen.WallLine(new Cell(5, 5), new Cell(8, 6)));
        Assert.Equal([new Cell(5, 5), new Cell(5, 4), new Cell(5, 3)], GameScreen.WallLine(new Cell(5, 5), new Cell(4, 3)));
        Assert.Equal([new Cell(5, 5)], GameScreen.WallLine(new Cell(5, 5), new Cell(5, 5)));
        Assert.Equal(GameScreen.MaxWallLine, GameScreen.WallLine(new Cell(0, 0), new Cell(500, 0)).Count);
    }

    [Fact]
    public void Dragging_with_the_wall_tool_lays_a_whole_line_and_keeps_the_tool_in_hand()
    {
        var ui = ReadyForWalls();
        var wall = ui.Simulation.Content.StructureIndex(BulwarkContent.Wall);
        var start = ui.FindSite(BulwarkContent.Wall);
        var end = new Cell(start.X + 5, start.Y + 1);
        var line = GameScreen.WallLine(start, end);
        var buildable = line.Count(cell => ui.Simulation.CheckPlacement(ui.Player, wall, cell) == PlacementResult.Ok);
        var credits = ui.Simulation.Players[ui.Player].Credits;
        Assert.True(buildable >= 3, $"Only {buildable} cells of the test line are buildable.");

        ui.ClickSidebar("WALL");
        ui.Drag(ui.PlacementPoint(wall, start), ui.PlacementPoint(wall, end));
        ui.Frame(1);

        Assert.Equal(buildable, WallsOf(ui));
        Assert.Equal(credits - (buildable * 50), ui.Simulation.Players[ui.Player].Credits);
        Assert.Equal(wall, ui.Screen.PlacingStructure);

        ui.Click(new Vector2(300, 300), MouseButton.Right);
        Assert.Equal(-1, ui.Screen.PlacingStructure);
    }

    [Fact]
    public void Walls_go_up_while_another_structure_is_under_construction()
    {
        var ui = ReadyForWalls();
        var plantType = ui.Simulation.Content.StructureIndex(BulwarkContent.PowerPlant);
        var wall = ui.Simulation.Content.StructureIndex(BulwarkContent.Wall);
        ui.ClickSidebar("POWER PLANT");
        ui.Click(ui.PlacementPoint(plantType, ui.FindSite(BulwarkContent.PowerPlant)));
        ui.Frame(1);
        var plant = ui.Simulation.Players[ui.Player].ConstructionSite;
        Assert.False(plant.IsNone);
        Assert.True(ui.Entry("WALL").Available);

        var site = ui.FindSite(BulwarkContent.Wall);
        ui.ClickSidebar("WALL");
        ui.Click(ui.PlacementPoint(wall, site));
        ui.Frame(20);

        Assert.Equal(1, WallsOf(ui));
        Assert.Equal(plant, ui.Simulation.Players[ui.Player].ConstructionSite); // the plant keeps the construction slot
        Assert.True(ui.Simulation.World.Get<StructureInfo>(ui.Structure(ui.Player, BulwarkContent.Wall)).IsComplete);
    }

    [Fact]
    public void A_line_laid_with_too_little_money_stops_where_the_money_does()
    {
        var ui = ReadyForWalls();
        var wall = ui.Simulation.Content.StructureIndex(BulwarkContent.Wall);
        var start = ui.FindSite(BulwarkContent.Wall);
        var end = new Cell(start.X + 6, start.Y);
        var buildable = GameScreen.WallLine(start, end).Count(cell => ui.Simulation.CheckPlacement(ui.Player, wall, cell) == PlacementResult.Ok);
        Assert.True(buildable >= 3);
        SetCredits(ui, 2 * 50);

        ui.ClickSidebar("WALL");
        ui.Drag(ui.PlacementPoint(wall, start), ui.PlacementPoint(wall, end));
        ui.Frame(1);

        Assert.Equal(2, WallsOf(ui));
        Assert.Equal(0, ui.Simulation.Players[ui.Player].Credits);
    }

    // A base with power and a barracks, so walls can be built.
    private static UiDriver ReadyForWalls()
    {
        var ui = new UiDriver();
        ui.Build(BulwarkContent.PowerPlant, "POWER PLANT");
        ui.Build(BulwarkContent.Barracks, "BARRACKS");
        return ui;
    }

    private static int WallsOf(UiDriver ui)
    {
        var count = 0;
        var world = ui.Simulation.World;
        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            var isWall = ui.Simulation.Content.Structure(world.Store<StructureInfo>().Get(index).StructureType).Id == BulwarkContent.Wall;
            count += isWall && world.Store<Owner>().Get(index).PlayerId == ui.Player ? 1 : 0;
        }

        return count;
    }

    private static void SetCredits(UiDriver ui, int credits)
    {
        var player = ui.Simulation.Players[ui.Player];
        typeof(Simulation.Players.PlayerState).GetProperty(nameof(player.Credits))!.SetValue(player, credits);
    }
}
