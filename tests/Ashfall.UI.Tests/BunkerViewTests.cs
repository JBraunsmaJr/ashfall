using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Map;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

/// <summary>Garrisoning from the interface: right-click your bunker with infantry selected; select it and unload.</summary>
public class BunkerViewTests
{
    [Fact]
    public void Right_clicking_a_bunker_sends_infantry_in_and_unload_lets_them_out()
    {
        var ui = new UiDriver();
        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);
        var origin = ui.Simulation.World.Get<StructureInfo>(yard).Origin;
        var bunker = ui.Simulation.PlaceStructure(ui.Player, ui.Simulation.Content.StructureIndex(BulwarkContent.Bunker), new Cell(origin.X + 5, origin.Y));
        var rifleman = ui.Simulation.SpawnUnit(ui.Player, ui.Simulation.Content.UnitIndex(BulwarkContent.Rifleman),
            ui.Simulation.World.Get<Position>(yard).Value + new Ashfall.Core.FixVec2(-3, 3));
        ui.Frame(1);

        ui.Click(ui.ScreenOf(rifleman));
        Assert.Equal(new[] { rifleman }, ui.Screen.Selection);
        ui.Click(ui.ScreenOf(bunker), MouseButton.Right);
        ui.RunUntil(s => s.IsSheltered(rifleman), 300);
        ui.Frame();
        Assert.Empty(ui.Screen.Selection); // a unit inside is not on the field to select

        ui.Click(ui.ScreenOf(bunker));
        Assert.Equal(new[] { bunker }, ui.Screen.Selection);
        Assert.True(ui.Screen.Sidebar.ShowUnload);
        var (x, y, width, height) = ui.Screen.Sidebar.UnloadRect();
        ui.Click(new Vector2(x + (width / 2), y + (height / 2)));
        ui.Frame(1);
        Assert.False(ui.Simulation.IsSheltered(rifleman));
        Assert.Contains("UNLOADING", ui.Screen.Announcements);
    }
}
