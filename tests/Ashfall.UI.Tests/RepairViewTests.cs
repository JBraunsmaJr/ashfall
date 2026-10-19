using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation.Components;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

/// <summary>Switching repair on and off for selected structures: the sidebar button or R.</summary>
public class RepairViewTests
{
    [Fact]
    public void Selected_structures_get_a_repair_button_and_r_toggles_it()
    {
        var ui = new UiDriver();
        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);
        ui.Simulation.World.Get<Health>(yard).Current = 900;
        ui.Frame(1); // one tick: the fog is computed
        ui.Click(ui.ScreenOf(yard)); // the middle of the building, where a player clicks
        Assert.True(ui.Screen.Sidebar.ShowRepair, "no repair button");
        Assert.False(ui.Screen.Sidebar.ShowStances);
        Assert.False(ui.Screen.Sidebar.RepairOn);

        var (x, y, width, height) = ui.Screen.Sidebar.RepairRect();
        ui.Click(new Vector2(x + (width / 2), y + (height / 2)));
        ui.Frame(1);
        Assert.True(ui.Simulation.World.Get<StructureInfo>(yard).Repairing, "not repairing");
        Assert.True(ui.Screen.Sidebar.RepairOn, "button off");
        Assert.Contains("REPAIRING", ui.Screen.Announcements);

        ui.Frame(40);
        Assert.True(ui.Simulation.World.Get<Health>(yard).Current > 900, "not mended");

        ui.Press(Key.R);
        ui.Frame(1);
        Assert.False(ui.Simulation.World.Get<StructureInfo>(yard).Repairing);
        Assert.Contains("REPAIR STOPPED", ui.Screen.Announcements);
    }
}
