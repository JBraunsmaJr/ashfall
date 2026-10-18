using System.Globalization;
using System.Numerics;
using Ashfall.Content;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Session;
using Ashfall.Session.Commands;
using Ashfall.Simulation;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Map;
using Ashfall.UI.Drawing;
using Ashfall.UI.Hud;
using Ashfall.UI.Input;
using Ashfall.UI.Text;
using Ashfall.UI.World;

namespace Ashfall.UI;

/// <summary>
/// The in-match screen: reads input, issues the local player's commands, moves the camera, and builds
/// the frame's scene (world, effects, overlays, sidebar). It never changes the simulation directly;
/// every action goes through <see cref="LocalOrders"/>, exactly as a remote player's would.
/// </summary>
public sealed class GameScreen
{
    // Pixels the mouse must travel with the button down before a click becomes a drag box.
    private const float DragThreshold = 6f;

    // Pixels from the window edge that scroll the camera.
    private const float EdgeScrollMargin = 4f;
    private const float PanSpeed = 900f;
    private const double DoubleTapSeconds = 0.4;

    private static readonly GameSpeed[] Speeds = [GameSpeed.Ratio(1, 2), GameSpeed.RealTime, GameSpeed.Times(2), GameSpeed.Times(4)];

    private readonly HeadlessMatch _match;
    private readonly LocalOrders? _orders;
    private readonly WorldBatch _batch = new();
    private readonly FogField _fog = new();
    private readonly MotionTracker _motion = new();
    private readonly Effects _effects = new();
    private readonly WorldPainter _painter;
    private readonly Sidebar _sidebar = new();
    private readonly List<Entity> _selection = [];
    private readonly HashSet<Entity> _selectedSet = [];
    private readonly List<(string Text, double Until, LinearColor Color)> _messages = [];
    private readonly List<Entity> _scratch = [];

    private Vector2 _framebuffer = new(1280, 720);
    private Vector2? _dragStart;
    private Vector2 _lastMouse;
    private int _placing = -1;
    private bool _attackMove;
    private int _speedIndex = 1;
    private int _lastGroup = -1;
    private double _lastGroupTime = double.MinValue;
    private double _now;
    private double _lastAttackWarning = double.MinValue;
    private bool _wasLowPower;
    private Entity _lastSite = Entity.None;
    private int _ownUnitsLastTick = -1;
    private readonly Dictionary<Entity, int> _structureHealth = [];

    public GameScreen(HeadlessMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        _match = match;
        _orders = match.LocalPlayer;
        Player = _orders?.PlayerId ?? -1;
        _painter = new WorldPainter(_batch, _fog, _motion);
        Camera = new IsoCamera(match.Simulation.Map.Width, match.Simulation.Map.Height);
        var home = HomeCenter();
        if (home is { } center)
        {
            Camera.CenterOn(center);
        }

        _fog.Update(Simulation.Visibility, Player, Simulation.Map.Width, Simulation.Map.Height);
        _motion.Capture(Simulation);
        for (var i = 0; i < Speeds.Length; i++)
        {
            if (Speeds[i] == match.Clock.Speed)
            {
                _speedIndex = i;
            }
        }
    }

    /// <summary>The local player's id, or -1 when spectating AIs.</summary>
    public int Player { get; }

    public IsoCamera Camera { get; }

    public ControlGroups Groups { get; } = new();

    public IReadOnlyList<Entity> Selection => _selection;

    public bool Paused { get; private set; }

    /// <summary>Set when the player asks to leave (Esc after the match ends).</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>The structure type being placed, or -1.</summary>
    public int PlacingStructure => _placing;

    public bool AttackMoveArmed => _attackMove;

    public Sidebar Sidebar => _sidebar;

    private GameSimulation Simulation => _match.Simulation;

    /// <summary>Call after every simulation tick: tracks motion, turns events into effects and messages.</summary>
    public void OnTick()
    {
        _motion.Capture(Simulation);
        foreach (var shot in _motion.Shots)
        {
            var visible = shot.Owner == Player || _fog.Sample(shot.From.X, shot.From.Y) >= 0.75f || _fog.Sample(shot.To.X, shot.To.Y) >= 0.75f;
            if (visible)
            {
                var heavy = Simulation.Content.Unit(shot.UnitType).Weapon?.Warhead == Warhead.AntiArmour;
                _effects.Shot(shot, heavy, _now);
            }
        }

        foreach (var death in _motion.Deaths)
        {
            if (_fog.Sample(death.Position.X, death.Position.Y) >= 0.5f)
            {
                _effects.Death(death, _now);
            }
        }

        _motion.Shots.Clear();
        _motion.Deaths.Clear();
        _fog.Update(Simulation.Visibility, Player, Simulation.Map.Width, Simulation.Map.Height);
        Announce();
    }

    /// <summary>Handles one frame of input. <paramref name="framebuffer"/> is the drawable size in pixels.</summary>
    public void Update(InputState input, Vector2 framebuffer)
    {
        ArgumentNullException.ThrowIfNull(input);
        _framebuffer = framebuffer;
        _now = input.Time;
        _sidebar.Update(Simulation, Player, framebuffer, _placing);
        Camera.ViewportOrigin = Vector2.Zero;
        Camera.ViewportSize = new Vector2(_sidebar.Layout.X, framebuffer.Y);
        PruneSelection();

        HandleKeys(input);
        HandleCamera(input);

        var overSidebar = _sidebar.Layout.Contains(input.Mouse);
        if (overSidebar && _dragStart is null)
        {
            HandleSidebar(input);
        }
        else
        {
            HandleWorld(input);
        }

        _lastMouse = input.Mouse;
    }

    /// <summary>Builds the frame: world (sorted), effects, overlays, then the sidebar on top.</summary>
    /// <param name="alpha">How far the frame is between the previous tick and the current one (0..1).</param>
    public void Draw(FrameScene scene, float alpha, double time)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _now = time;
        _sidebar.Update(Simulation, Player, _framebuffer, _placing);
        scene.Clear();
        scene.ClearColor = Palette.Clear;
        scene.Time = (float)time;
        scene.SunDirection = Vector3.Normalize(new Vector3(-0.55f, 0.2f, 0.8f));
        // Late-afternoon light: a low warm sun and a cool ambient, dim enough for base lights,
        // muzzle flashes and Cindrite glow to read.
        scene.SunColor = new LinearColor(0.62f, 0.52f, 0.40f);
        scene.Ambient = new LinearColor(0.20f, 0.22f, 0.30f);

        _batch.Camera = Camera;
        _batch.Visibility = _fog.Enabled ? _fog.Sample : null;
        _painter.PaintTerrain(Simulation, Camera);
        _painter.PaintStructures(Simulation, Player, _selectedSet, scene);
        _painter.PaintUnits(Simulation, Player, _selectedSet, Math.Clamp(alpha, 0f, 1f));
        if (_placing >= 0 && !_sidebar.Layout.Contains(_lastMouse))
        {
            var origin = PlacementOrigin(_lastMouse);
            _painter.PaintPlacement(Simulation, _placing, origin, Simulation.CheckPlacement(Player, _placing, origin) == PlacementResult.Ok);
        }

        _effects.Draw(_batch, scene, time);
        _batch.FlushTo(scene);

        var canvas = new Canvas(scene);
        DrawOverlays(canvas, alpha);
        _sidebar.Draw(canvas, Simulation, Player, _lastMouse, StatusLine());
        Minimap.Draw(canvas, Simulation, Player, _fog, Camera, _sidebar.Layout.Minimap);
        DrawMessages(canvas);
        DrawGameOver(canvas);
    }

    /// <summary>Replaces the selection (own entities only).</summary>
    public void Select(IEnumerable<Entity> entities, bool add = false)
    {
        if (!add)
        {
            _selection.Clear();
            _selectedSet.Clear();
        }

        foreach (var entity in entities)
        {
            if (Simulation.World.IsAlive(entity) && OwnerOf(entity) == Player && _selectedSet.Add(entity))
            {
                _selection.Add(entity);
            }
        }
    }

    private void HandleKeys(InputState input)
    {
        if (input.WasPressed(Key.Escape))
        {
            if (Simulation.IsOver)
            {
                ExitRequested = true;
            }
            else if (_placing >= 0 || _attackMove)
            {
                _placing = -1;
                _attackMove = false;
            }
            else
            {
                Select([]);
            }
        }

        if (input.WasPressed(Key.P))
        {
            Paused = !Paused;
        }

        if (input.WasPressed(Key.Plus) || input.WasPressed(Key.Minus))
        {
            _speedIndex = Math.Clamp(_speedIndex + (input.WasPressed(Key.Plus) ? 1 : -1), 0, Speeds.Length - 1);
            _match.SetSpeed(Speeds[_speedIndex]);
            Say("SPEED " + Speeds[_speedIndex].ToString().ToUpperInvariant(), Palette.Text);
        }

        if (input.WasPressed(Key.H) && HomeCenter() is { } home)
        {
            Camera.CenterOn(home);
        }

        if (_orders is null)
        {
            return;
        }

        if (input.WasPressed(Key.A) && HasArmedSelection())
        {
            _attackMove = true;
            _placing = -1;
        }

        if (input.WasPressed(Key.S))
        {
            foreach (var unit in MovableSelection())
            {
                _orders.Enqueue(Command.Stop(Player, unit));
            }
        }

        for (var group = 0; group <= 9; group++)
        {
            if (!input.WasPressed(Key.D0 + group))
            {
                continue;
            }

            if (input.Control)
            {
                Groups.Assign(group, _selection);
                Say("GROUP " + group.ToString(CultureInfo.InvariantCulture) + " SET", Palette.Text);
            }
            else
            {
                Groups.Prune(Simulation.World.IsAlive);
                Select(Groups.Get(group), input.Shift);
                if (_lastGroup == group && input.Time - _lastGroupTime < DoubleTapSeconds && SelectionCenter() is { } center)
                {
                    Camera.CenterOn(center);
                }

                _lastGroup = group;
                _lastGroupTime = input.Time;
            }
        }
    }

    private void HandleCamera(InputState input)
    {
        var pan = Vector2.Zero;
        if (input.IsHeld(Key.Left))
        {
            pan.X -= 1;
        }

        if (input.IsHeld(Key.Right))
        {
            pan.X += 1;
        }

        if (input.IsHeld(Key.Up))
        {
            pan.Y -= 1;
        }

        if (input.IsHeld(Key.Down))
        {
            pan.Y += 1;
        }

        if (input.MouseInWindow && _dragStart is null && !input.IsDown(MouseButton.Middle))
        {
            if (input.Mouse.X <= EdgeScrollMargin)
            {
                pan.X -= 1;
            }
            else if (input.Mouse.X >= _framebuffer.X - EdgeScrollMargin)
            {
                pan.X += 1;
            }

            if (input.Mouse.Y <= EdgeScrollMargin)
            {
                pan.Y -= 1;
            }
            else if (input.Mouse.Y >= _framebuffer.Y - EdgeScrollMargin)
            {
                pan.Y += 1;
            }
        }

        if (pan != Vector2.Zero)
        {
            Camera.PanPixels(Vector2.Normalize(pan) * PanSpeed * input.DeltaSeconds);
        }

        if (input.IsDown(MouseButton.Middle) && !input.WasPressed(MouseButton.Middle))
        {
            Camera.PanPixels(_lastMouse - input.Mouse);
        }

        if (input.Scroll != 0 && !_sidebar.Layout.Contains(input.Mouse))
        {
            Camera.StepZoom(Math.Sign(input.Scroll), input.Mouse);
        }
    }

    private void HandleSidebar(InputState input)
    {
        if (_sidebar.MinimapContains(input.Mouse))
        {
            var target = Minimap.ToWorld(input.Mouse, Simulation, _sidebar.Layout.Minimap);
            if (input.IsDown(MouseButton.Left))
            {
                Camera.CenterOn(target);
            }
            else if (input.WasPressed(MouseButton.Right) && _orders is not null && _selection.Count > 0)
            {
                IssueMove(target, attackMove: false);
            }

            return;
        }

        if (!input.WasPressed(MouseButton.Left) || _orders is null)
        {
            return;
        }

        var entry = _sidebar.HitTest(input.Mouse);
        if (entry is null)
        {
            return;
        }

        if (!entry.Available)
        {
            Say(entry.Reason ?? "UNAVAILABLE", Palette.TextWarn);
            return;
        }

        if (!entry.Affordable)
        {
            Say("INSUFFICIENT FUNDS", Palette.TextWarn);
            return;
        }

        if (entry.IsStructure)
        {
            _placing = _placing == entry.TypeIndex ? -1 : entry.TypeIndex;
            _attackMove = false;
            return;
        }

        var producer = PickProducer(entry.TypeIndex);
        if (producer.IsNone)
        {
            Say("QUEUE FULL", Palette.TextWarn);
            return;
        }

        _orders.Enqueue(Command.Train(Player, producer, entry.TypeIndex));
    }

    private void HandleWorld(InputState input)
    {
        if (_placing >= 0)
        {
            if (input.WasPressed(MouseButton.Left) && _orders is not null)
            {
                var origin = PlacementOrigin(input.Mouse);
                var result = Simulation.CheckPlacement(Player, _placing, origin);
                if (result == PlacementResult.Ok)
                {
                    _orders.Enqueue(Command.Build(Player, _placing, origin));
                    _placing = -1;
                }
                else
                {
                    Say(Describe(result), Palette.TextWarn);
                }
            }
            else if (input.WasPressed(MouseButton.Right))
            {
                _placing = -1;
            }

            return;
        }

        if (_attackMove)
        {
            if (input.WasPressed(MouseButton.Left))
            {
                IssueMove(Camera.ScreenToGround(input.Mouse), attackMove: true);
                _attackMove = input.Shift;
            }
            else if (input.WasPressed(MouseButton.Right))
            {
                _attackMove = false;
            }

            return;
        }

        if (input.WasPressed(MouseButton.Left))
        {
            _dragStart = input.Mouse;
        }

        if (input.WasReleased(MouseButton.Left) && _dragStart is { } start)
        {
            _dragStart = null;
            if (Vector2.Distance(start, input.Mouse) > DragThreshold)
            {
                BoxSelect(start, input.Mouse, input.Shift);
            }
            else
            {
                ClickSelect(input.Mouse, input.Shift);
            }
        }

        if (input.WasPressed(MouseButton.Right) && _orders is not null && _selection.Count > 0)
        {
            var target = PickEntity(input.Mouse);
            if (!target.IsNone && OwnerOf(target) >= 0 && Simulation.AreEnemies(Player, OwnerOf(target)))
            {
                IssueAttack(target);
            }
            else
            {
                IssueMove(Camera.ScreenToGround(input.Mouse), attackMove: false);
            }
        }
    }

    private void IssueAttack(Entity target)
    {
        var anyArmed = false;
        foreach (var unit in MovableSelection())
        {
            if (Simulation.World.Has<Armament>(unit))
            {
                _orders!.Enqueue(Command.Attack(Player, unit, target));
                anyArmed = true;
            }
        }

        if (anyArmed)
        {
            _effects.Marker(MotionTracker.ToVector(Simulation.ClosestPointOf(target, Simulation.World.Get<Position>(target).Value)), attack: true, _now);
        }
    }

    private void IssueMove(Vector2 target, bool attackMove)
    {
        _scratch.Clear();
        _scratch.AddRange(MovableSelection());
        if (_scratch.Count == 0)
        {
            return;
        }

        // A loose square formation centred on the click, so units don't all aim for one point.
        var side = (int)MathF.Ceiling(MathF.Sqrt(_scratch.Count));
        const float spacing = 0.8f;
        for (var i = 0; i < _scratch.Count; i++)
        {
            var offset = new Vector2((i % side) - ((side - 1) / 2f), (i / side) - ((side - 1) / 2f)) * spacing;
            var point = target + offset;
            var fixedPoint = new Core.FixVec2(ToFix(point.X), ToFix(point.Y));
            var unit = _scratch[i];
            _orders!.Enqueue(attackMove && Simulation.World.Has<Armament>(unit)
                ? Command.AttackMove(Player, unit, fixedPoint)
                : Command.Move(Player, unit, fixedPoint));
        }

        _effects.Marker(target, attackMove, _now);
    }

    private void ClickSelect(Vector2 mouse, bool add)
    {
        var picked = PickEntity(mouse);
        if (picked.IsNone || OwnerOf(picked) != Player)
        {
            if (!add)
            {
                Select([]);
            }

            return;
        }

        if (add && _selectedSet.Contains(picked))
        {
            _selectedSet.Remove(picked);
            _selection.Remove(picked);
            return;
        }

        Select([picked], add);
    }

    private void BoxSelect(Vector2 a, Vector2 b, bool add)
    {
        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        var world = Simulation.World;
        _scratch.Clear();
        foreach (var index in world.Store<UnitInfo>().EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId != Player)
            {
                continue;
            }

            var entity = world.EntityAt(index);
            var position = _motion.PositionOf(entity, MotionTracker.ToVector(world.Store<Position>().Get(index).Value), 1f);
            var screen = Camera.WorldToScreen(new Vector3(position, 0.2f));
            if (screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y)
            {
                _scratch.Add(entity);
            }
        }

        // Prefer combat units: a box over the base shouldn't grab harvesters along with the army.
        if (_scratch.Any(entity => world.Has<Armament>(entity)))
        {
            _scratch.RemoveAll(entity => !world.Has<Armament>(entity));
        }

        Select(_scratch, add);
    }

    /// <summary>The unit or structure under a screen point: units first (nearest centre), then the structure on that cell.</summary>
    private Entity PickEntity(Vector2 mouse)
    {
        var world = Simulation.World;
        var best = Entity.None;
        var bestDistance = float.MaxValue;
        foreach (var index in world.Store<UnitInfo>().EntityIndices)
        {
            var entity = world.EntityAt(index);
            var owner = world.Store<Owner>().Get(index).PlayerId;
            var position = _motion.PositionOf(entity, MotionTracker.ToVector(world.Store<Position>().Get(index).Value), 1f);
            if (owner != Player && _fog.Sample(position.X, position.Y) < 0.75f)
            {
                continue;
            }

            var definition = Simulation.Content.Unit(world.Store<UnitInfo>().Get(index).UnitType);
            var radius = (definition.IsHarvester || definition.Id == BulwarkContent.Warden ? 20f : 12f) * Camera.Zoom;
            var distance = Vector2.Distance(Camera.WorldToScreen(new Vector3(position, 0.2f)), mouse);
            if (distance < radius && distance < bestDistance)
            {
                best = entity;
                bestDistance = distance;
            }
        }

        if (!best.IsNone)
        {
            return best;
        }

        var ground = Camera.ScreenToGround(mouse);
        var cell = new Cell((int)MathF.Floor(ground.X), (int)MathF.Floor(ground.Y));
        var structureIndex = Simulation.Map.StructureAt(cell);
        if (structureIndex < 0 || _fog.At(cell.X, cell.Y) <= 0f)
        {
            return Entity.None;
        }

        var structure = world.EntityAt(structureIndex);
        return world.IsAlive(structure) ? structure : Entity.None;
    }

    private Entity PickProducer(int unitType)
    {
        var world = Simulation.World;
        var role = Simulation.Content.Unit(unitType).ProducedBy;
        var best = Entity.None;
        var bestQueue = int.MaxValue;
        foreach (var index in world.Store<Producer>().EntityIndices)
        {
            var info = world.Store<StructureInfo>().Get(index);
            var producer = world.Store<Producer>().Get(index);
            if (world.Store<Owner>().Get(index).PlayerId != Player || !info.IsComplete || producer.IsFull
                || Simulation.Content.Structure(info.StructureType).Role != role)
            {
                continue;
            }

            if (producer.QueueCount < bestQueue)
            {
                best = world.EntityAt(index);
                bestQueue = producer.QueueCount;
            }
        }

        return best;
    }

    private Cell PlacementOrigin(Vector2 mouse)
    {
        var definition = Simulation.Content.Structure(_placing);
        var ground = Camera.ScreenToGround(mouse);
        return new Cell((int)MathF.Floor(ground.X - (definition.Width / 2f) + 0.5f), (int)MathF.Floor(ground.Y - (definition.Height / 2f) + 0.5f));
    }

    private void DrawOverlays(Canvas canvas, float alpha)
    {
        var world = Simulation.World;

        // Health bars over selected and damaged units.
        foreach (var index in world.Store<Health>().EntityIndices)
        {
            var entity = world.EntityAt(index);
            var health = world.Store<Health>().Get(index);
            var selected = _selectedSet.Contains(entity);
            if (!selected && health.Current >= health.Max)
            {
                continue;
            }

            Vector3 anchor;
            float width;
            if (world.Has<StructureInfo>(entity))
            {
                var info = world.Get<StructureInfo>(entity);
                var definition = Simulation.Content.Structure(info.StructureType);
                if (OwnerOf(entity) != Player && _fog.At(info.Origin.X, info.Origin.Y) <= 0f)
                {
                    continue;
                }

                anchor = new Vector3(info.Origin.X + (definition.Width / 2f), info.Origin.Y + (definition.Height / 2f), WorldPainter.StructureHeight(definition.Role) + 0.9f);
                width = 16f * definition.Width;
            }
            else
            {
                var position = _motion.PositionOf(entity, MotionTracker.ToVector(world.Get<Position>(entity).Value), alpha);
                if (OwnerOf(entity) != Player && _fog.Sample(position.X, position.Y) < 0.75f)
                {
                    continue;
                }

                var definition = Simulation.Content.Unit(world.Get<UnitInfo>(entity).UnitType);
                anchor = new Vector3(position, definition.IsHarvester || definition.Id == BulwarkContent.Warden ? 0.8f : 0.7f);
                width = definition.IsHarvester || definition.Id == BulwarkContent.Warden ? 30f : 18f;
            }

            var screen = Camera.WorldToScreen(anchor);
            width *= Camera.Zoom;
            var height = 3f * Math.Max(1f, Camera.Zoom);
            var fraction = Math.Clamp(health.Current / (float)health.Max, 0f, 1f);
            var color = fraction > 0.6f ? Palette.HealthGood : fraction > 0.3f ? Palette.HealthMid : Palette.HealthLow;
            canvas.Rect(screen.X - (width / 2) - 1, screen.Y - 1, width + 2, height + 2, new LinearColor(0, 0, 0, 0.7f));
            canvas.Rect(screen.X - (width / 2), screen.Y, width * fraction, height, color);
        }

        // Drag box.
        if (_dragStart is { } start && Vector2.Distance(start, _lastMouse) > DragThreshold)
        {
            var min = Vector2.Min(start, _lastMouse);
            var max = Vector2.Max(start, _lastMouse);
            canvas.Rect(min.X, min.Y, max.X - min.X, max.Y - min.Y, Palette.WithAlpha(Palette.Selection, 0.12f));
            canvas.Frame(min.X, min.Y, max.X - min.X, max.Y - min.Y, 1f, Palette.Selection);
        }

        // Mode hint at the cursor.
        if (_attackMove)
        {
            canvas.TextShadowed(_lastMouse.X + 14, _lastMouse.Y + 10, "ATTACK-MOVE", 2f, Palette.HealthLow);
        }

        // Help and status, bottom-left of the view.
        var scale = _sidebar.Layout.Scale;
        var help = "RMB MOVE/ATTACK  A ATTACK-MOVE  S STOP  CTRL+0-9 GROUP  H HOME  +/- SPEED  P PAUSE";
        canvas.TextShadowed(8 * scale, _framebuffer.Y - (14 * scale), help, scale, Palette.TextDim);
        var tick = Simulation.Tick;
        var clock = TimeSpan.FromSeconds(tick / (double)SimulationConstants.TicksPerSecond);
        var top = string.Create(CultureInfo.InvariantCulture, $"{(int)clock.TotalMinutes:00}:{clock.Seconds:00}  {_match.Clock.Speed}{(Paused ? "  PAUSED" : string.Empty)}");
        canvas.TextShadowed(8 * scale, 8 * scale, top.ToUpperInvariant(), 2f * scale, Paused ? Palette.Credits : Palette.Text);
    }

    private void DrawMessages(Canvas canvas)
    {
        _messages.RemoveAll(message => message.Until < _now);
        var scale = 2f * _sidebar.Layout.Scale;
        var y = 40f * _sidebar.Layout.Scale;
        foreach (var (text, until, color) in _messages)
        {
            var fade = (float)Math.Clamp(until - _now, 0, 1);
            canvas.Text(
                (_sidebar.Layout.X / 2f) - (PixelFont.Measure(text, scale) / 2f) + scale,
                y + scale,
                text,
                scale,
                new LinearColor(0, 0, 0, 0.8f * fade));
            canvas.TextCentered(_sidebar.Layout.X / 2f, y, text, scale, Palette.WithAlpha(color, fade));
            y += 12f * scale;
        }
    }

    private void DrawGameOver(Canvas canvas)
    {
        if (!Simulation.IsOver)
        {
            return;
        }

        var won = Player >= 0 && Simulation.WinningTeam == Simulation.Players[Player].Team;
        var title = Player < 0 ? "TEAM " + Simulation.WinningTeam.ToString(CultureInfo.InvariantCulture) + " WINS"
            : won ? "VICTORY" : "DEFEAT";
        var middle = _sidebar.Layout.X / 2f;
        var y = _framebuffer.Y * 0.38f;
        var scale = 8f * _sidebar.Layout.Scale;
        canvas.Rect(0, y - (16 * _sidebar.Layout.Scale), _sidebar.Layout.X, (PixelFont.GlyphHeight * scale) + (52 * _sidebar.Layout.Scale), new LinearColor(0, 0, 0, 0.6f));
        canvas.TextCentered(middle, y, title, scale, won || Player < 0 ? Palette.TextGood : Palette.TextWarn);
        canvas.TextCentered(middle, y + (PixelFont.GlyphHeight * scale) + (12 * _sidebar.Layout.Scale), "PRESS ESC TO LEAVE", 2f * _sidebar.Layout.Scale, Palette.Text);
    }

    private string? StatusLine()
    {
        if (_selection.Count == 0)
        {
            return null;
        }

        if (_selection.Count == 1)
        {
            var entity = _selection[0];
            var world = Simulation.World;
            var name = world.Has<UnitInfo>(entity)
                ? Simulation.Content.Unit(world.Get<UnitInfo>(entity).UnitType).Name
                : Simulation.Content.Structure(world.Get<StructureInfo>(entity).StructureType).Name;
            var health = world.Get<Health>(entity);
            return string.Create(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()} {health.Current}/{health.Max}");
        }

        return _selection.Count.ToString(CultureInfo.InvariantCulture) + " SELECTED";
    }

    private void Announce()
    {
        if (Player < 0)
        {
            return;
        }

        var state = Simulation.Players[Player];
        var world = Simulation.World;

        if (!_lastSite.IsNone && state.ConstructionSite.IsNone && world.IsAlive(_lastSite))
        {
            Say("CONSTRUCTION COMPLETE", Palette.TextGood);
        }

        _lastSite = state.ConstructionSite;

        var ownUnits = 0;
        foreach (var index in world.Store<UnitInfo>().EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId == Player)
            {
                ownUnits++;
            }
        }

        if (_ownUnitsLastTick >= 0 && ownUnits > _ownUnitsLastTick)
        {
            Say("UNIT READY", Palette.TextGood);
        }

        _ownUnitsLastTick = ownUnits;

        if (state.HasLowPower && !_wasLowPower)
        {
            Say("LOW POWER", Palette.TextWarn);
        }

        _wasLowPower = state.HasLowPower;

        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId != Player)
            {
                continue;
            }

            var entity = world.EntityAt(index);
            var health = world.Store<Health>().Get(index).Current;
            if (_structureHealth.TryGetValue(entity, out var before) && health < before && _now - _lastAttackWarning > 10)
            {
                Say("BASE UNDER ATTACK", Palette.TextWarn);
                _lastAttackWarning = _now;
            }

            _structureHealth[entity] = health;
        }
    }

    private void Say(string text, LinearColor color)
    {
        _messages.RemoveAll(message => message.Text == text);
        _messages.Add((text, _now + 2.5, color));
        if (_messages.Count > 4)
        {
            _messages.RemoveAt(0);
        }
    }

    private static string Describe(PlacementResult result) => result switch
    {
        PlacementResult.CannotAfford => "INSUFFICIENT FUNDS",
        PlacementResult.Blocked => "CANNOT BUILD THERE",
        PlacementResult.TooFarFromBase => "TOO FAR FROM BASE",
        PlacementResult.AlreadyBuilding => "ALREADY BUILDING",
        PlacementResult.MissingPrerequisite => "MISSING PREREQUISITE",
        PlacementResult.NoConstructionYard => "NO CONSTRUCTION YARD",
        _ => "CANNOT BUILD",
    };

    private void PruneSelection()
    {
        var world = Simulation.World;
        if (_selection.RemoveAll(entity => !world.IsAlive(entity)) > 0)
        {
            _selectedSet.RemoveWhere(entity => !world.IsAlive(entity));
        }
    }

    private IEnumerable<Entity> MovableSelection()
    {
        foreach (var entity in _selection)
        {
            if (Simulation.World.IsAlive(entity) && Simulation.World.Has<Mover>(entity) && OwnerOf(entity) == Player)
            {
                yield return entity;
            }
        }
    }

    private bool HasArmedSelection() => _selection.Any(entity => Simulation.World.IsAlive(entity) && Simulation.World.Has<Armament>(entity));

    private int OwnerOf(Entity entity) =>
        Simulation.World.IsAlive(entity) && Simulation.World.Has<Owner>(entity) ? Simulation.World.Get<Owner>(entity).PlayerId : -1;

    private Vector2? SelectionCenter()
    {
        if (_selection.Count == 0)
        {
            return null;
        }

        var sum = Vector2.Zero;
        foreach (var entity in _selection)
        {
            sum += MotionTracker.ToVector(Simulation.World.Get<Position>(entity).Value);
        }

        return sum / _selection.Count;
    }

    private Vector2? HomeCenter()
    {
        var world = Simulation.World;
        Vector2? any = null;
        foreach (var index in world.Store<StructureInfo>().EntityIndices)
        {
            if (world.Store<Owner>().Get(index).PlayerId != Player && Player >= 0)
            {
                continue;
            }

            var position = MotionTracker.ToVector(world.Store<Position>().Get(index).Value);
            if (Simulation.Content.Structure(world.Store<StructureInfo>().Get(index).StructureType).Role == StructureRole.ConstructionYard)
            {
                return position;
            }

            any ??= position;
        }

        return any;
    }

    private static Core.Fix64 ToFix(float value) => Core.Fix64.FromRaw((long)Math.Round(value * 4294967296.0));
}
