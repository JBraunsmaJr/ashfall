using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation.Components;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

/// <summary>Setting a rally point: select a barracks or factory and right-click the ground.</summary>
public class RallyViewTests
{
    [Fact]
    public void Right_clicking_the_ground_with_a_barracks_selected_sets_its_rally_point()
    {
        var ui = new UiDriver();
        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);
        var origin = ui.Simulation.World.Get<StructureInfo>(yard).Origin;
        var barracks = ui.Simulation.PlaceStructure(ui.Player, ui.Simulation.Content.StructureIndex(BulwarkContent.Barracks),
            new Ashfall.Simulation.Map.Cell(origin.X + 5, origin.Y));
        ui.Frame(1);

        ui.Click(ui.ScreenOf(barracks));
        Assert.Equal(new[] { barracks }, ui.Screen.Selection);
        var ground = ui.ScreenOf(barracks) + new Vector2(-120, 60);
        ui.Click(ground, MouseButton.Right);
        ui.Frame(1);

        var producer = ui.Simulation.World.Get<Producer>(barracks);
        Assert.True(producer.HasRally);
        var expected = ui.Screen.Camera.PickGround(ground, (x, y) => Ashfall.UI.World.WorldPainter.GroundHeight(ui.Simulation.Map, x, y));
        Assert.True(Vector2.Distance(expected, UiDriver.ToVector(producer.Rally)) < 0.05f);
        Assert.Contains("RALLY POINT SET", ui.Screen.Announcements);
    }
}
