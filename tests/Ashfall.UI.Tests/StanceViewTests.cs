using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Orders;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

/// <summary>Setting a stance from the sidebar's stance buttons or the G key.</summary>
public class StanceViewTests
{
    [Fact]
    public void Stance_buttons_show_for_armed_units_and_set_the_stance_of_the_whole_selection()
    {
        var ui = new UiDriver();
        Assert.False(ui.Screen.Sidebar.ShowStances);

        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);
        var at = ui.Simulation.World.Get<Position>(yard).Value;
        var type = ui.Simulation.Content.UnitIndex(BulwarkContent.Rifleman);
        var a = ui.Simulation.SpawnUnit(0, type, at + new Core.FixVec2(-3, 3));
        var b = ui.Simulation.SpawnUnit(0, type, at + new Core.FixVec2(-1, 4));
        ui.Frame(3);
        ui.Drag(ui.ScreenOf(a) - new Vector2(30, 30), ui.ScreenOf(b) + new Vector2(30, 30));
        Assert.Equal(2, ui.Screen.Selection.Count);
        Assert.True(ui.Screen.Sidebar.ShowStances);
        Assert.Equal(UnitStance.Guard, ui.Screen.Sidebar.CurrentStance);

        var (x, y, width, height) = ui.Screen.Sidebar.StanceRect(UnitStance.Free);
        ui.Click(new Vector2(x + (width / 2), y + (height / 2)));
        ui.Frame(1);
        Assert.Equal(UnitStance.Free, ui.Simulation.World.Get<Armament>(a).Stance);
        Assert.Equal(UnitStance.Free, ui.Simulation.World.Get<Armament>(b).Stance);
        Assert.Equal(new[] { a, b }.Order(), ui.Screen.Selection.Order()); // clicking a stance keeps the selection

        // G cycles: free -> guard -> hold ground.
        ui.Press(Key.G);
        ui.Frame(1);
        Assert.Equal(UnitStance.Guard, ui.Simulation.World.Get<Armament>(a).Stance);
        ui.Press(Key.G);
        ui.Frame(1);
        Assert.Equal(UnitStance.HoldGround, ui.Simulation.World.Get<Armament>(b).Stance);
        Assert.Equal(UnitStance.HoldGround, ui.Screen.Sidebar.CurrentStance);
        Assert.Contains("STANCE: HOLD GROUND", ui.Screen.Announcements);
    }
}
