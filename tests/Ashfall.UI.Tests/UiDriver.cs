using System.Numerics;
using Ashfall.Content;
using Ashfall.Rendering.Scene;
using Ashfall.Session;
using Ashfall.Simulation;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.UI.Hud;
using Ashfall.UI.Input;

namespace Ashfall.UI.Tests;

/// <summary>
/// Plays a match through the interface exactly as a person would, with mouse and keyboard input
/// only: no command is ever built by hand. Every frame runs Update, the due ticks, and Draw.
/// </summary>
internal sealed class UiDriver
{
    public static readonly Vector2 Framebuffer = new(1280, 720);

    // The mouse rests mid-view: at the window edge it would scroll the camera every frame.
    private readonly InputState _input = new() { Mouse = new Vector2(500, 360) };
    private double _time;

    public UiDriver(PlayerController opponent = PlayerController.IdleAi, ulong seed = 7)
        : this(MatchSpec.Skirmish(seed).WithController(0, PlayerController.Human).WithController(1, opponent))
    {
    }

    /// <summary>Any match with a human seat: a skirmish, or the swarm fortress against the Maw.</summary>
    public UiDriver(MatchSpec spec)
    {
        Match = HeadlessMatch.Start(spec, GameSpeed.Unthrottled);
        Screen = new GameScreen(Match);
        Frame();
    }

    public HeadlessMatch Match { get; }

    public GameScreen Screen { get; }

    public FrameScene Scene { get; } = new();

    public GameSimulation Simulation => Match.Simulation;

    public Session.Commands.LocalOrders Orders => Match.LocalPlayer!;

    public int Player => Screen.Player;

    /// <summary>One frame: input, then <paramref name="ticks"/> simulation ticks, then drawing.</summary>
    public void Frame(int ticks = 0)
    {
        _time += 1 / 60.0;
        _input.Time = _time;
        _input.DeltaSeconds = 1 / 60f;
        Screen.Update(_input, Framebuffer);
        _input.EndFrame();
        Tick(ticks);
        Screen.Draw(Scene, 1f, _time);
    }

    public void Tick(int ticks)
    {
        for (var i = 0; i < ticks && !Simulation.IsOver; i++)
        {
            Match.Step();
            Screen.OnTick();
        }
    }

    /// <summary>Runs (drawing every 15 ticks) until the condition holds.</summary>
    public void RunUntil(Func<GameSimulation, bool> condition, int maxTicks)
    {
        for (var ticks = 0; ticks < maxTicks; ticks += 15)
        {
            if (condition(Simulation))
            {
                return;
            }

            Frame(15);
        }

        Assert.True(condition(Simulation), $"Condition not met within {maxTicks} ticks (tick {Simulation.Tick}).");
    }

    public void MoveMouse(Vector2 position)
    {
        _input.Mouse = position;
        Frame();
    }

    public void Click(Vector2 position, MouseButton button = MouseButton.Left)
    {
        _input.Mouse = position;
        _input.SetButton(button, true);
        Frame();
        _input.SetButton(button, false);
        Frame();
    }

    public void Drag(Vector2 from, Vector2 to)
    {
        _input.Mouse = from;
        _input.SetButton(MouseButton.Left, true);
        Frame();
        _input.Mouse = to;
        Frame();
        _input.SetButton(MouseButton.Left, false);
        Frame();
    }

    public void Press(Key key, Key? modifier = null)
    {
        if (modifier is { } held)
        {
            _input.SetKey(held, true);
        }

        _input.SetKey(key, true);
        Frame();
        _input.SetKey(key, false);
        if (modifier is { } released)
        {
            _input.SetKey(released, false);
        }

        Frame();
    }

    /// <summary>Holds a key (Shift, Control) across the following frames until <see cref="KeyUp"/>.</summary>
    public void KeyDown(Key key) => _input.SetKey(key, true);

    public void KeyUp(Key key) => _input.SetKey(key, false);

    public void ClickSidebar(string label)
    {
        var entries = Screen.Sidebar.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Label == label)
            {
                ShowTab(entries[i].Tab);
                var (x, y, width, height) = Screen.Sidebar.ButtonRect(i);
                Click(new Vector2(x + (width / 2), y + (height / 2)));
                return;
            }
        }

        Assert.Fail($"No sidebar button '{label}'. Buttons: {string.Join(", ", entries.Select(e => e.Label))}");
    }

    /// <summary>Clicks a sidebar tab, as a player does, unless it is already on show.</summary>
    public void ShowTab(SidebarTab tab)
    {
        if (Screen.Sidebar.ActiveTab != tab)
        {
            var (x, y, width, height) = Screen.Sidebar.TabRect(tab);
            Click(new Vector2(x + (width / 2), y + (height / 2)));
            Assert.Equal(tab, Screen.Sidebar.ActiveTab);
        }
    }

    public SidebarEntry Entry(string label) => Screen.Sidebar.Entries.Single(entry => entry.Label == label);

    /// <summary>The screen point that places a structure of <paramref name="structureType"/> with its footprint at <paramref name="origin"/>.</summary>
    public Vector2 PlacementPoint(int structureType, Cell origin)
    {
        var definition = Simulation.Content.Structure(structureType);
        return Screen.Camera.WorldToScreen(new Vector2(origin.X + (definition.Width / 2f), origin.Y + (definition.Height / 2f)));
    }

    /// <summary>A free spot for <paramref name="structureId"/> near the player's base that is on screen.</summary>
    public Cell FindSite(string structureId)
    {
        var type = Simulation.Content.StructureIndex(structureId);
        var home = DuelMapHome();
        for (var radius = 3; radius < 10; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var origin = new Cell(home.X + dx, home.Y + dy);
                    if (Simulation.CheckPlacement(Player, type, origin) == PlacementResult.Ok
                        && Screen.Camera.IsInViewport(PlacementPoint(type, origin))
                        && !Screen.Sidebar.Layout.Contains(PlacementPoint(type, origin)))
                    {
                        return origin;
                    }
                }
            }
        }

        Assert.Fail($"No site for {structureId}.");
        return default;
    }

    /// <summary>Builds a structure the way a player does: sidebar button, then a click on the map. Waits for completion.</summary>
    public void Build(string structureId, string label)
    {
        var site = FindSite(structureId);
        var type = Simulation.Content.StructureIndex(structureId);
        ClickSidebar(label);
        Assert.Equal(type, Screen.PlacingStructure);
        Click(PlacementPoint(type, site));
        Assert.Equal(-1, Screen.PlacingStructure);
        Frame(1);
        Assert.True(Simulation.World.IsAlive(Simulation.Players[Player].ConstructionSite), $"{structureId} was not started.");
        RunUntil(s => s.Players[Player].ConstructionSite.IsNone, 2_000);
    }

    public Vector2 ScreenOf(Entity entity) =>
        Screen.Camera.WorldToScreen(new Vector3(ToVector(Simulation.World.Get<Position>(entity).Value), 0.2f));

    public List<Entity> Units(int player, string? unitId = null)
    {
        var world = Simulation.World;
        var units = new List<Entity>();
        foreach (var index in world.Store<UnitInfo>().EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId == player
                && (unitId is null || Simulation.Content.Unit(world.Store<UnitInfo>().Get(index).UnitType).Id == unitId))
            {
                units.Add(world.EntityAt(index));
            }
        }

        return units;
    }

    public Entity Structure(int player, string structureId)
    {
        var world = Simulation.World;
        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId == player
                && Simulation.Content.Structure(world.Store<StructureInfo>().Get(index).StructureType).Id == structureId)
            {
                return world.EntityAt(index);
            }
        }

        return Entity.None;
    }

    public static Vector2 ToVector(Core.FixVec2 value) => new((float)(value.X.RawValue / 4294967296.0), (float)(value.Y.RawValue / 4294967296.0));

    private Cell DuelMapHome()
    {
        var yard = Structure(Player, BulwarkContent.ConstructionYard);
        return Simulation.World.Get<StructureInfo>(yard).Origin + new Cell(1, 1);
    }
}
