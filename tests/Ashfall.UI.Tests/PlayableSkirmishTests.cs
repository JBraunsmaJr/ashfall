using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation.Components;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

/// <summary>
/// The M2 exit gate, "playable skirmish against a dummy AI", played end to end through the interface:
/// build a base from the sidebar, train an army, box-select it, scroll to the enemy with the radar and
/// attack-move into their base until the victory banner. Mouse and keyboard input only.
/// </summary>
public class PlayableSkirmishTests
{
    [Fact]
    public void A_player_using_only_the_interface_beats_the_dummy_ai()
    {
        var ui = new UiDriver();

        ui.Build(BulwarkContent.PowerPlant, "POWER PLANT");
        ui.Build(BulwarkContent.Barracks, "BARRACKS");
        Assert.True(ui.Entry("ROCKETEER").Available);

        for (var i = 0; i < 5; i++)
        {
            ui.ClickSidebar("ROCKETEER");
        }

        ui.ClickSidebar("RIFLEMAN");
        ui.Frame(1);
        Assert.Equal(5, ui.Entry("ROCKETEER").Queued); // the barracks queue holds five...
        Assert.Equal(0, ui.Entry("RIFLEMAN").Queued);  // ...so the sixth order was refused

        ui.RunUntil(_ => ui.Entry("ROCKETEER").Queued == 4, 400);
        ui.ClickSidebar("RIFLEMAN");
        ui.RunUntil(s => ui.Units(ui.Player).Count >= 6, 3_000);

        // Box-select everything on screen: the six new soldiers.
        ui.Press(Key.H);
        ui.Drag(new Vector2(4, 30), new Vector2(ui.Screen.Sidebar.Layout.X - 4, 700));
        Assert.Equal(6, ui.Screen.Selection.Count);
        ui.Press(Key.D1, Key.Control);

        // Jump to the enemy base on the radar and attack-move onto their construction yard.
        var enemyYard = ui.Structure(1, BulwarkContent.ConstructionYard);
        var target = UiDriver.ToVector(ui.Simulation.World.Get<Position>(enemyYard).Value);
        var (x, y, size) = ui.Screen.Sidebar.Layout.Minimap;
        var cell = size / ui.Simulation.Map.Width;
        ui.Click(new Vector2(x + (target.X * cell), y + (target.Y * cell)));
        Assert.True(Vector2.Distance(ui.Screen.Camera.Center, target) < 1.5f, $"Camera at {ui.Screen.Camera.Center}, wanted {target}.");

        ui.Press(Key.D1);
        Assert.Equal(6, ui.Screen.Selection.Count);
        ui.Press(Key.Q);
        Assert.True(ui.Screen.AttackMoveArmed);
        ui.Click(ui.Screen.Camera.WorldToScreen(target));

        ui.RunUntil(s => s.IsOver, 6_000);
        Assert.Equal(0, ui.Simulation.WinningTeam);
        Assert.True(ui.Scene.QuadCount > 0);

        ui.Press(Key.Escape);
        Assert.True(ui.Screen.ExitRequested);
    }
}
