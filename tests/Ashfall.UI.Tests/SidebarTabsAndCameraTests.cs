using System.Numerics;
using Ashfall.Content;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Orders;
using Ashfall.UI.Hud;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

public class SidebarTabsAndCameraTests
{
    [Theory]
    [InlineData(Key.W, 0f, -1f)]
    [InlineData(Key.S, 0f, 1f)]
    [InlineData(Key.A, -1f, 0f)]
    [InlineData(Key.D, 1f, 0f)]
    public void Wasd_pans_the_camera_like_the_arrow_keys(Key key, float screenX, float screenY)
    {
        var ui = new UiDriver();
        var camera = ui.Screen.Camera;
        camera.CenterOn(new Vector2(ui.Simulation.Map.Width / 2f, ui.Simulation.Map.Height / 2f)); // room to pan every way
        ui.MoveMouse(new Vector2(400, 300)); // away from the edges, so only the key pans
        var before = camera.Center;

        ui.KeyDown(key);
        for (var i = 0; i < 20; i++)
        {
            ui.Frame();
        }

        ui.KeyUp(key);

        // The view moved the way the key points: on screen, the new centre lies that way from the old one.
        var moved = camera.WorldToScreen(camera.Center) - camera.WorldToScreen(before);
        Assert.True(moved.Length() > 10f, $"{key} barely moved the view ({moved}).");
        Assert.True(Vector2.Dot(Vector2.Normalize(moved), new Vector2(screenX, screenY)) > 0.99f, $"{key} moved the view by {moved}.");
    }

    [Fact]
    public void A_and_s_pan_instead_of_ordering_and_control_keeps_them_still()
    {
        var ui = new UiDriver();
        var type = ui.Simulation.Content.UnitIndex(BulwarkContent.Rifleman);
        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);
        var rifleman = ui.Simulation.SpawnUnit(ui.Player, type, ui.Simulation.World.Get<Position>(yard).Value + new Core.FixVec2(-3, 3));
        ui.Frame(3);
        ui.Click(ui.ScreenOf(rifleman));
        Assert.Equal(new[] { rifleman }, ui.Screen.Selection);

        ui.Press(Key.A);
        Assert.False(ui.Screen.AttackMoveArmed);

        var center = ui.Screen.Camera.Center;
        ui.KeyDown(Key.Control);
        ui.KeyDown(Key.S);
        ui.Frame();
        ui.Frame();
        ui.KeyUp(Key.S);
        ui.KeyUp(Key.Control);
        Assert.Equal(center, ui.Screen.Camera.Center);
        Assert.Equal(OrderKind.Idle, ui.Simulation.World.Get<UnitOrder>(rifleman).Kind);
    }

    [Fact]
    public void Every_cameo_has_a_tab_by_role()
    {
        var ui = new UiDriver();
        var tabs = ui.Screen.Sidebar.Entries.ToDictionary(entry => entry.Id, entry => entry.Tab);

        Assert.Equal(SidebarTab.Base, tabs[BulwarkContent.PowerPlant]);
        Assert.Equal(SidebarTab.Base, tabs[BulwarkContent.Refinery]);
        Assert.Equal(SidebarTab.Base, tabs[BulwarkContent.Barracks]);
        Assert.Equal(SidebarTab.Base, tabs[BulwarkContent.Factory]);
        Assert.Equal(SidebarTab.Defence, tabs[BulwarkContent.Wall]);
        Assert.Equal(SidebarTab.Infantry, tabs[BulwarkContent.Rifleman]);
        Assert.Equal(SidebarTab.Infantry, tabs[BulwarkContent.Rocketeer]);
        Assert.Equal(SidebarTab.Infantry, tabs[BulwarkContent.Pyro]);
        Assert.Equal(SidebarTab.Vehicles, tabs[BulwarkContent.Warden]);
        Assert.Equal(SidebarTab.Vehicles, tabs[BulwarkContent.Harvester]);
        Assert.Equal(SidebarTab.Infantry, tabs[BulwarkContent.Medic]);
        Assert.Equal(SidebarTab.Base, tabs[BulwarkContent.RepairBay]);
        Assert.DoesNotContain(BulwarkContent.ConstructionYard, tabs.Keys);
        Assert.Equal("Warden light tank", ui.Entry("WARDEN").Name);
    }

    [Fact]
    public void Only_the_tab_on_show_can_be_clicked_and_tab_cycles_through_them()
    {
        var ui = new UiDriver();
        var sidebar = ui.Screen.Sidebar;
        Assert.Equal(SidebarTab.Base, sidebar.ActiveTab);

        // The rifleman's cameo shares a grid slot with the power plant's; with Base on show the click is the power plant.
        var rifleman = sidebar.Entries.ToList().FindIndex(entry => entry.Label == "RIFLEMAN");
        var (x, y, width, height) = sidebar.ButtonRect(rifleman);
        var point = new Vector2(x + (width / 2), y + (height / 2));
        Assert.Equal("POWER PLANT", sidebar.HitTest(point)!.Label);

        ui.Press(Key.Tab);
        Assert.Equal(SidebarTab.Defence, sidebar.ActiveTab);
        ui.Press(Key.Tab);
        Assert.Equal(SidebarTab.Infantry, sidebar.ActiveTab);
        Assert.Equal("RIFLEMAN", sidebar.HitTest(point)!.Label);
        ui.Press(Key.Tab, Key.Shift);
        Assert.Equal(SidebarTab.Defence, sidebar.ActiveTab);
        ui.Press(Key.Tab);
        ui.Press(Key.Tab);
        ui.Press(Key.Tab);
        Assert.Equal(SidebarTab.Base, sidebar.ActiveTab); // wraps around

        ui.ShowTab(SidebarTab.Vehicles);
        Assert.Equal("WARDEN", sidebar.HitTest(point)!.Label);
    }

    [Fact]
    public void A_tab_whose_cameos_come_available_is_marked_until_it_is_shown()
    {
        var ui = new UiDriver();
        var sidebar = ui.Screen.Sidebar;
        Assert.False(sidebar.NeedsAttention(SidebarTab.Infantry));

        ui.Build(BulwarkContent.PowerPlant, "POWER PLANT");
        ui.Build(BulwarkContent.Barracks, "BARRACKS");
        ui.Frame();

        Assert.True(sidebar.NeedsAttention(SidebarTab.Infantry));
        Assert.True(sidebar.NeedsAttention(SidebarTab.Defence)); // the wall needs a barracks too
        Assert.False(sidebar.NeedsAttention(SidebarTab.Vehicles));

        ui.ClickSidebar("RIFLEMAN");
        ui.Frame(1);
        Assert.Equal(SidebarTab.Infantry, sidebar.ActiveTab);
        Assert.False(sidebar.NeedsAttention(SidebarTab.Infantry));
        Assert.True(sidebar.NeedsAttention(SidebarTab.Defence));
        Assert.Equal(1, ui.Entry("RIFLEMAN").Queued);
    }

    [Fact]
    public void Every_tab_draws_its_cameos()
    {
        var ui = new UiDriver();
        var counts = new List<int>();
        foreach (var tab in Enum.GetValues<SidebarTab>())
        {
            ui.ShowTab(tab);
            ui.MoveMouse(new Vector2(400, 300));
            ui.Frame();
            counts.Add(ui.Scene.QuadCount);
        }

        // Base (four cameos) draws more than Defence (one).
        Assert.True(counts[(int)SidebarTab.Base] > counts[(int)SidebarTab.Defence], string.Join(", ", counts));
    }
}
