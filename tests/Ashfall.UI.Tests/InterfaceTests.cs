using System.Numerics;
using Ashfall.Content;
using Ashfall.Session;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Orders;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

public class InterfaceTests
{
    [Fact]
    public void The_camera_starts_over_the_players_base()
    {
        var ui = new UiDriver();
        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);

        Assert.True(Vector2.Distance(ui.Screen.Camera.Center, UiDriver.ToVector(ui.Simulation.World.Get<Position>(yard).Value)) < 0.01f);
    }

    [Fact]
    public void Clicking_selects_shift_clicking_toggles_and_escape_clears()
    {
        var ui = new UiDriver();
        var (a, b) = TwoRiflemen(ui);

        ui.Click(ui.ScreenOf(a));
        Assert.Equal(new[] { a }, ui.Screen.Selection);

        ui.KeyDown(Key.Shift);
        ui.Click(ui.ScreenOf(b));
        Assert.Equal(new[] { a, b }, ui.Screen.Selection);

        ui.Click(ui.ScreenOf(a));
        Assert.Equal(new[] { b }, ui.Screen.Selection);
        ui.KeyUp(Key.Shift);

        ui.Press(Key.Escape);
        Assert.Empty(ui.Screen.Selection);
    }

    [Fact]
    public void Enemy_units_cannot_be_selected()
    {
        var ui = new UiDriver();
        var enemy = ui.Simulation.SpawnUnit(1, ui.Simulation.Content.UnitIndex(BulwarkContent.Rifleman), Near(ui, 2, 2));
        ui.Frame(3);

        ui.Click(ui.ScreenOf(enemy));

        Assert.Empty(ui.Screen.Selection);
    }

    [Fact]
    public void A_drag_box_selects_combat_units_in_preference_to_harvesters()
    {
        var ui = new UiDriver();
        var (a, b) = TwoRiflemen(ui);
        var harvester = ui.Simulation.SpawnUnit(0, ui.Simulation.Content.UnitIndex(BulwarkContent.Harvester), Near(ui, 3, 1));
        ui.Frame(1);

        ui.Drag(new Vector2(2, 2), new Vector2(ui.Screen.Sidebar.Layout.X - 2, 718));

        Assert.Equal(2, ui.Screen.Selection.Count);
        Assert.DoesNotContain(harvester, ui.Screen.Selection);

        ui.Drag(ui.ScreenOf(harvester) - new Vector2(12, 12), ui.ScreenOf(harvester) + new Vector2(12, 12));
        Assert.Equal(new[] { harvester }, ui.Screen.Selection);
        _ = (a, b);
    }

    [Fact]
    public void Right_clicking_the_ground_orders_a_move_and_an_enemy_an_attack()
    {
        var ui = new UiDriver();
        var (a, b) = TwoRiflemen(ui);
        var enemy = ui.Simulation.SpawnUnit(1, ui.Simulation.Content.UnitIndex(BulwarkContent.Harvester), Near(ui, 4, 4));
        ui.Frame(3);
        ui.Drag(ui.ScreenOf(a) - new Vector2(40, 40), ui.ScreenOf(b) + new Vector2(40, 40));
        Assert.Equal(2, ui.Screen.Selection.Count);

        ui.Click(NearScreen(ui, -2, 3), MouseButton.Right);
        ui.Frame(1);
        Assert.Equal(OrderKind.Move, ui.Simulation.World.Get<UnitOrder>(a).Kind);
        Assert.Equal(OrderKind.Move, ui.Simulation.World.Get<UnitOrder>(b).Kind);
        Assert.NotEqual(ui.Simulation.World.Get<Mover>(a).Target, ui.Simulation.World.Get<Mover>(b).Target); // formation, not a pile

        ui.Click(ui.ScreenOf(enemy), MouseButton.Right);
        ui.Frame(1);
        Assert.Equal(OrderKind.Attack, ui.Simulation.World.Get<UnitOrder>(a).Kind);
        Assert.Equal(enemy, ui.Simulation.World.Get<UnitOrder>(a).Target);
    }

    [Fact]
    public void A_then_click_attack_moves_and_s_stops()
    {
        var ui = new UiDriver();
        var (a, _) = TwoRiflemen(ui);
        ui.Click(ui.ScreenOf(a));

        ui.Press(Key.A);
        Assert.True(ui.Screen.AttackMoveArmed);
        ui.Click(NearScreen(ui, 6, 6));
        ui.Frame(1);
        Assert.False(ui.Screen.AttackMoveArmed);
        Assert.Equal(OrderKind.AttackMove, ui.Simulation.World.Get<UnitOrder>(a).Kind);

        ui.Press(Key.S);
        ui.Frame(1);
        Assert.Equal(OrderKind.Idle, ui.Simulation.World.Get<UnitOrder>(a).Kind);
        Assert.False(ui.Simulation.World.Get<Mover>(a).HasTarget);
    }

    [Fact]
    public void Control_groups_store_and_recall_selections()
    {
        var ui = new UiDriver();
        var (a, b) = TwoRiflemen(ui);
        ui.Click(ui.ScreenOf(a));
        ui.Press(Key.D3, Key.Control);
        ui.Click(ui.ScreenOf(b));
        Assert.Equal(new[] { b }, ui.Screen.Selection);

        ui.Press(Key.D3);

        Assert.Equal(new[] { a }, ui.Screen.Selection);
        Assert.Equal(new[] { a }, ui.Screen.Groups.Get(3));
    }

    [Fact]
    public void Destroyed_units_drop_out_of_the_selection()
    {
        var ui = new UiDriver();
        var (a, b) = TwoRiflemen(ui);
        ui.Drag(ui.ScreenOf(a) - new Vector2(40, 40), ui.ScreenOf(b) + new Vector2(40, 40));
        ui.Simulation.World.Get<Health>(a) = ui.Simulation.World.Get<Health>(a) with { Current = 0 };

        ui.Frame(1);
        ui.Frame();

        Assert.Equal(new[] { b }, ui.Screen.Selection);
    }

    [Fact]
    public void Sidebar_buttons_explain_what_they_need()
    {
        var ui = new UiDriver();

        Assert.True(ui.Entry("POWER PLANT").Available);
        Assert.False(ui.Entry("REFINERY").Available);
        Assert.Equal("NEEDS POWER PLANT", ui.Entry("REFINERY").Reason);
        Assert.Equal("NEEDS BARRACKS", ui.Entry("RIFLEMAN").Reason);
        Assert.Equal("NEEDS WAR FACTORY", ui.Entry("WARDEN").Reason);

        ui.ClickSidebar("REFINERY");
        Assert.Equal(-1, ui.Screen.PlacingStructure);
    }

    [Fact]
    public void Placing_a_structure_shows_progress_and_blocks_other_construction()
    {
        var ui = new UiDriver();
        var site = ui.FindSite(BulwarkContent.PowerPlant);
        var type = ui.Simulation.Content.StructureIndex(BulwarkContent.PowerPlant);

        ui.ClickSidebar("POWER PLANT");
        ui.Click(ui.PlacementPoint(type, site));
        ui.Frame(40);

        Assert.InRange(ui.Entry("POWER PLANT").Progress ?? -1f, 0.2f, 0.4f);
        Assert.Equal("BUILDING", ui.Entry("POWER PLANT").Reason);
        Assert.False(ui.Entry("POWER PLANT").Available); // one structure at a time
        Assert.Equal(5_000 - 300, ui.Simulation.Players[ui.Player].Credits);
    }

    [Fact]
    public void Clicking_an_invalid_spot_keeps_placement_mode_and_right_click_cancels_it()
    {
        var ui = new UiDriver();
        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);
        var type = ui.Simulation.Content.StructureIndex(BulwarkContent.PowerPlant);

        ui.ClickSidebar("POWER PLANT");
        ui.Click(ui.ScreenOf(yard)); // on top of the construction yard
        ui.Frame(1);

        Assert.Equal(type, ui.Screen.PlacingStructure);
        Assert.True(ui.Simulation.Players[ui.Player].ConstructionSite.IsNone);

        ui.Click(new Vector2(300, 300), MouseButton.Right);
        Assert.Equal(-1, ui.Screen.PlacingStructure);
    }

    [Fact]
    public void Training_queues_at_the_least_busy_producer()
    {
        var ui = new UiDriver();
        ui.Build(BulwarkContent.PowerPlant, "POWER PLANT");
        ui.Build(BulwarkContent.Barracks, "BARRACKS");

        ui.ClickSidebar("RIFLEMAN");
        ui.ClickSidebar("RIFLEMAN");
        ui.Frame(1);

        Assert.Equal(2, ui.Entry("RIFLEMAN").Queued);
        Assert.Equal(5_000 - 300 - 300 - 300, ui.Simulation.Players[ui.Player].Credits);
        ui.RunUntil(_ => ui.Units(ui.Player, BulwarkContent.Rifleman).Count == 2, 400);
    }

    [Fact]
    public void Enemies_in_the_shroud_are_not_drawn_or_targetable()
    {
        var ui = new UiDriver();
        var (a, _) = TwoRiflemen(ui);
        ui.Click(ui.ScreenOf(a));
        var far = new Vector2(40, 40);
        var hidden = ui.Simulation.SpawnUnit(1, ui.Simulation.Content.UnitIndex(BulwarkContent.Harvester), Core.FixVec2.FromInts(40, 40));
        ui.Frame(3);
        ui.Screen.Camera.CenterOn(far);
        ui.Frame();

        ui.Click(ui.Screen.Camera.WorldToScreen(far), MouseButton.Right);
        ui.Frame(1);

        Assert.Equal(OrderKind.Move, ui.Simulation.World.Get<UnitOrder>(a).Kind); // not an attack on what it can't see
        _ = hidden;
    }

    [Fact]
    public void Speed_and_pause_keys_change_the_session()
    {
        var ui = new UiDriver();

        ui.Press(Key.P);
        Assert.True(ui.Screen.Paused);
        ui.Press(Key.P);
        Assert.False(ui.Screen.Paused);

        ui.Press(Key.Plus);
        Assert.Equal(GameSpeed.Times(2), ui.Match.Clock.Speed);
        ui.Press(Key.Minus);
        ui.Press(Key.Minus);
        Assert.Equal(GameSpeed.Ratio(1, 2), ui.Match.Clock.Speed);
    }

    [Fact]
    public void Drawing_a_busy_frame_produces_sane_geometry()
    {
        var ui = new UiDriver(PlayerController.ScriptedAi);
        ui.Screen.Camera.CenterOn(new Vector2(32, 32));

        ui.Frame(3_000);

        Assert.InRange(ui.Scene.QuadCount, 1_000, 60_000);
        Assert.InRange(ui.Scene.Lights.Count, 1, Rendering.Scene.FrameScene.MaxLights);
        foreach (var vertex in ui.Scene.Vertices)
        {
            Assert.True(float.IsFinite(vertex.Screen.X) && float.IsFinite(vertex.Screen.Y), "Non-finite vertex position.");
            Assert.InRange(vertex.Extra.Z, 0f, 1f);
        }
    }

    private static (Entity A, Entity B) TwoRiflemen(UiDriver ui)
    {
        var type = ui.Simulation.Content.UnitIndex(BulwarkContent.Rifleman);
        var a = ui.Simulation.SpawnUnit(0, type, Near(ui, -3, 3));
        var b = ui.Simulation.SpawnUnit(0, type, Near(ui, -1, 4));
        ui.Frame(3);
        return (a, b);
    }

    // A point near the player's construction yard, offset in cells.
    private static Core.FixVec2 Near(UiDriver ui, int dx, int dy)
    {
        var yard = ui.Structure(ui.Player, BulwarkContent.ConstructionYard);
        var center = ui.Simulation.World.Get<Position>(yard).Value;
        return center + Core.FixVec2.FromInts(dx, dy);
    }

    private static Vector2 NearScreen(UiDriver ui, int dx, int dy) => ui.Screen.Camera.WorldToScreen(UiDriver.ToVector(Near(ui, dx, dy)));
}
