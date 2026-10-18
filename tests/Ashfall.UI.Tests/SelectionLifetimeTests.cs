using Ashfall.Content;
using Ashfall.Simulation.Components;

namespace Ashfall.UI.Tests;

/// <summary>Selected things die between frames; the view must shrug that off.</summary>
public class SelectionLifetimeTests
{
    [Fact]
    public void A_selected_unit_that_dies_between_input_and_drawing_does_not_break_the_frame()
    {
        var ui = new UiDriver();
        var rifleman = ui.Simulation.SpawnUnit(ui.Player, ui.Simulation.Content.UnitIndex(BulwarkContent.Rifleman), ui.Simulation.World.Get<Position>(ui.Structure(ui.Player, BulwarkContent.ConstructionYard)).Value);
        ui.Frame(1);
        ui.Screen.Select([rifleman]);
        ui.Simulation.World.Get<Health>(rifleman).Current = 0;

        ui.Frame(1); // the unit dies during this frame's tick, after input and before drawing

        Assert.False(ui.Simulation.World.IsAlive(rifleman));
        ui.Frame(1);
        Assert.Empty(ui.Screen.Selection);
    }

    [Fact]
    public void A_selected_structure_that_dies_between_input_and_drawing_does_not_break_the_frame()
    {
        var ui = new UiDriver();
        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);
        ui.Screen.Select([yard]);
        ui.Simulation.World.Get<Health>(yard).Current = 0;

        ui.Frame(1);
        ui.Frame(1);

        Assert.Empty(ui.Screen.Selection);
    }
}
