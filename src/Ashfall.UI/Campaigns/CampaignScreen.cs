﻿using System.Globalization;
using System.Numerics;
using Ashfall.Campaign;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Simulation.Horde;
using Ashfall.UI.Drawing;
using Ashfall.UI.Input;
using Ashfall.UI.Text;

namespace Ashfall.UI.Campaigns;

/// <summary>What the player decided on the campaign screen.</summary>
public enum CampaignChoiceKind
{
    /// <summary>Play the mission for <see cref="CampaignChoice.Target"/>.</summary>
    Launch,

    /// <summary>Save and go back to the main menu.</summary>
    Leave,
}

/// <summary>A decision on the campaign screen, handed to the client.</summary>
/// <param name="Difficulty">The difficulty to play on: the campaign's, or lower if the player lowered it.</param>
public sealed record CampaignChoice(CampaignChoiceKind Kind, CampaignTarget Target, MawDifficulty Difficulty);

/// <summary>
/// The campaign screen (GDD section 7): the world map with regions coloured by who holds them and how infested they
/// are, hives, home, events and the global infestation meter; a panel with the selected region or event, the mission
/// it would become and a launch button; the events waiting for an answer; and what the last turn did. Click a region
/// or event to select it; Esc saves and leaves. It runs no simulation: everything shown comes from the campaign state.
/// </summary>
public sealed class CampaignScreen
{
    private readonly CampaignState _campaign;
    private readonly List<Widget> _widgets = [];
    private readonly List<(float X, float Y, string Text, float Size, LinearColor Color)> _labels = [];
    private Vector2 _framebuffer = new(1280, 720);
    private Vector2 _mouse;
    private double _time;
    private CampaignTarget? _selected;
    private MawDifficulty _difficulty;

    public CampaignScreen(CampaignState campaign, int allies, TurnReport? lastTurn = null, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        _campaign = campaign;
        _difficulty = campaign.Difficulty;
        Allies = allies;
        LastTurn = lastTurn;
        Message = message;
        _selected = campaign.Targets.Count > 0 ? CampaignAutopilot.ChooseTarget(campaign) : null;
    }

    public CampaignState Campaign => _campaign;

    /// <summary>Scripted-AI allies that join every mission.</summary>
    public int Allies { get; }

    /// <summary>What the last turn did, shown in the panel.</summary>
    public TurnReport? LastTurn { get; }

    /// <summary>A line shown at the top of the panel, e.g. that a mission was abandoned.</summary>
    public string? Message { get; }

    /// <summary>Set once the player launches a mission or leaves.</summary>
    public CampaignChoice? Choice { get; private set; }

    /// <summary>The region or event in the panel, if any.</summary>
    public CampaignTarget? Selected => _selected;

    /// <summary>The difficulty the next mission will be played on (lowered here, never raised).</summary>
    public MawDifficulty Difficulty => _difficulty;

    /// <summary>Every clickable thing in the panel with its label, centre and size, for tests and automation.</summary>
    public IEnumerable<(string Label, Vector2 Center, Vector2 Size, bool Enabled)> Targets
    {
        get
        {
            Layout();
            return _widgets.Select(w => (w.Text, new Vector2(w.X + (w.W / 2), w.Y + (w.H / 2)), new Vector2(w.W, w.H), w.Enabled)).ToList();
        }
    }

    private float Scale => _framebuffer.Y >= 1300 ? 2f : _framebuffer.Y >= 960 ? 1.5f : 1f;

    private float PanelWidth => Math.Min(_framebuffer.X * 0.42f, 400 * Scale);

    /// <summary>Where a region's marker is on screen.</summary>
    public Vector2 RegionOnScreen(int region)
    {
        var (left, top, scale) = MapTransform();
        var data = _campaign.World.Regions[region];
        return new Vector2(left + (data.X * scale), top + (data.Y * scale));
    }

    public void Update(InputState input, Vector2 framebuffer)
    {
        ArgumentNullException.ThrowIfNull(input);
        _framebuffer = framebuffer;
        _mouse = input.Mouse;
        _time = input.Time;
        if (Choice is not null)
        {
            return;
        }

        Layout();
        if (input.WasPressed(Key.Escape))
        {
            Leave();
            return;
        }

        if (input.WasPressed(Key.Enter))
        {
            if (_widgets.FirstOrDefault(w => w.Primary) is { Enabled: true, Click: { } launch })
            {
                launch();
            }

            return;
        }

        if (!input.WasPressed(MouseButton.Left))
        {
            return;
        }

        foreach (var widget in _widgets)
        {
            if (widget.Contains(input.Mouse))
            {
                if (widget.Enabled)
                {
                    widget.Click?.Invoke();
                }

                return;
            }
        }

        if (RegionAt(input.Mouse) is { } region)
        {
            var campaignEvent = _campaign.Events.FirstOrDefault(e => e.Region == region);
            _selected = campaignEvent is not null && !_campaign.CanTarget(CampaignTarget.Region(region), out _)
                ? CampaignTarget.Event(campaignEvent.Id)
                : CampaignTarget.Region(region);
        }
    }

    public void Draw(FrameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.Clear();
        scene.ClearColor = Palette.Clear;
        Layout();
        var canvas = new Canvas(scene);
        DrawMap(canvas);
        DrawPanel(canvas);
        DrawHeader(canvas);
        if (_campaign.Outcome != CampaignOutcome.InProgress)
        {
            DrawOutcome(canvas);
        }
    }

    // The world map fills the space left of the panel, below the header, keeping its proportions.
    private (float Left, float Top, float Scale) MapTransform()
    {
        var scale = Scale;
        var areaLeft = 16 * scale;
        var areaTop = 56 * scale;
        var areaWidth = _framebuffer.X - PanelWidth - (32 * scale);
        var areaHeight = _framebuffer.Y - areaTop - (16 * scale);
        var fit = Math.Min(areaWidth / WorldMap.Width, areaHeight / WorldMap.Height);
        var left = areaLeft + ((areaWidth - (WorldMap.Width * fit)) / 2);
        var top = areaTop + ((areaHeight - (WorldMap.Height * fit)) / 2);
        return (left, top, fit);
    }

    private float MarkerRadius => Math.Max(5f, 11f * MapTransform().Scale);

    private int? RegionAt(Vector2 point)
    {
        var radius = MarkerRadius * 1.6f;
        int? best = null;
        var bestDistance = radius * radius;
        foreach (var region in _campaign.World.Regions)
        {
            var distance = Vector2.DistanceSquared(point, RegionOnScreen(region.Id));
            if (distance <= bestDistance)
            {
                best = region.Id;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void DrawMap(Canvas canvas)
    {
        var world = _campaign.World;
        var (left, top, fit) = MapTransform();
        canvas.Rect(left, top, WorldMap.Width * fit, WorldMap.Height * fit, Palette.Mix(Palette.Clear, Palette.GroundDark, 0.35f));

        // Territory: a soft glow per region, blue where liberated, deepening to violet with infestation.
        var reach = 70f * fit;
        foreach (var region in world.Regions)
        {
            canvas.Disc(RegionOnScreen(region.Id), reach, reach, Palette.WithAlpha(StateColor(region.Id), 0.28f), QuadShape.Glow);
        }

        // Theatre names, faint, at each theatre's middle.
        foreach (var theatre in world.Theatres)
        {
            var middle = theatre.Regions.Aggregate(Vector2.Zero, (sum, id) => sum + RegionOnScreen(id)) / theatre.Regions.Count;
            canvas.TextCentered(middle.X, middle.Y - (34 * fit), theatre.Name.ToUpperInvariant(), Math.Max(1f, 2f * fit), Palette.WithAlpha(Palette.TextDim, 0.6f));
        }

        foreach (var (a, b) in world.Borders)
        {
            var front = (_campaign.IsLiberated(a) && !_campaign.IsLiberated(b)) || (_campaign.IsLiberated(b) && !_campaign.IsLiberated(a));
            canvas.Line(RegionOnScreen(a), RegionOnScreen(b), Math.Max(1f, (front ? 3f : 2f) * fit), front ? Palette.WithAlpha(Palette.Credits, 0.7f) : Palette.WithAlpha(Palette.TextDim, 0.45f));
        }

        var radius = MarkerRadius;
        var hovered = RegionAt(_mouse);
        var pulse = 0.5f + (0.5f * MathF.Sin((float)_time * 4f));
        foreach (var region in world.Regions)
        {
            var at = RegionOnScreen(region.Id);
            if (_campaign.IsHive(region.Id))
            {
                canvas.Disc(at, radius * 2.4f, radius * 2.4f, Palette.WithAlpha(Palette.MawGlow, 0.5f), QuadShape.Glow);
                canvas.Disc(at, radius * 1.55f, radius * 1.55f, Palette.MawGlow, QuadShape.Ring);
            }

            if (region.IsHome)
            {
                canvas.Disc(at, radius * 1.5f, radius * 1.5f, Palette.Text, QuadShape.Ring);
            }

            if (_campaign.CanTarget(CampaignTarget.Region(region.Id), out _))
            {
                canvas.Disc(at, radius * (1.35f + (0.2f * pulse)), radius * (1.35f + (0.2f * pulse)), Palette.WithAlpha(Palette.Text, 0.55f + (0.35f * pulse)), QuadShape.Ring);
            }

            canvas.Disc(at, radius + 2, radius + 2, Palette.Clear);
            canvas.Disc(at, radius, radius, StateColor(region.Id));
            var selected = _selected is { IsEvent: false } target && target.Id == region.Id
                || (_selected is { IsEvent: true } chosen && _campaign.FindEvent(chosen.Id)?.Region == region.Id);
            if (selected || hovered == region.Id)
            {
                canvas.Disc(at, radius * 1.9f, radius * 1.9f, selected ? Palette.Selection : Palette.WithAlpha(Palette.Text, 0.6f), QuadShape.Ring);
                var labelScale = Math.Max(1f, 1.5f * Scale);
                canvas.TextCentered(at.X + 1, at.Y + (radius * 2f) + 1, region.Name.ToUpperInvariant(), labelScale, Palette.WithAlpha(Palette.Clear, 0.9f));
                canvas.TextCentered(at.X, at.Y + (radius * 2f), region.Name.ToUpperInvariant(), labelScale, Palette.Text);
            }
        }

        // Events: a gold marker above the region with the turns left.
        foreach (var campaignEvent in _campaign.Events)
        {
            var at = RegionOnScreen(campaignEvent.Region) - new Vector2(0, radius * 2.3f);
            var left2 = campaignEvent.ExpiresAfterTurn - _campaign.Turn;
            canvas.Disc(at, radius * 1.4f, radius * 1.4f, Palette.WithAlpha(Palette.Credits, 0.35f + (0.3f * pulse)), QuadShape.Glow);
            canvas.Disc(at, radius * 0.75f, radius * 0.75f, Palette.Credits);
            var scale = Math.Max(1f, radius / 6f);
            canvas.TextCentered(at.X, at.Y - (PixelFont.GlyphHeight * scale / 2), left2.ToString(CultureInfo.InvariantCulture), scale, Palette.Clear);
        }
    }

    private LinearColor StateColor(int region)
    {
        if (_campaign.IsLiberated(region))
        {
            return Palette.Player(0);
        }

        return _campaign.InfestationOf(region) switch
        {
            1 => LinearColor.FromSrgb(214, 206, 150),
            2 => LinearColor.FromSrgb(232, 150, 40),
            3 => LinearColor.FromSrgb(214, 70, 40),
            4 => LinearColor.FromSrgb(150, 30, 50),
            _ => Palette.MawViolet,
        };
    }

    private void DrawHeader(Canvas canvas)
    {
        var scale = Scale;
        canvas.Rect(0, 0, _framebuffer.X - PanelWidth, 48 * scale, Palette.Panel);
        var title = string.Create(CultureInfo.InvariantCulture,
            $"CAMPAIGN {_campaign.Code}  TURN {_campaign.Turn}  {_campaign.World.Regions.Count(r => _campaign.IsLiberated(r.Id))}/{_campaign.World.Regions.Count} LIBERATED  {_campaign.HivesLeft} HIVE{(_campaign.HivesLeft == 1 ? string.Empty : "S")} LEFT");
        canvas.Text(12 * scale, 8 * scale, title, 2 * scale, Palette.Text);

        // The global infestation meter.
        var barLeft = 12 * scale;
        var barWidth = _framebuffer.X - PanelWidth - (24 * scale) - (130 * scale);
        var barTop = 30 * scale;
        canvas.Rect(barLeft, barTop, barWidth, 10 * scale, Palette.Button);
        var meter = _campaign.Meter / 100f;
        canvas.Rect(barLeft, barTop, barWidth * meter, 10 * scale, meter >= 0.8f ? Palette.MawGlow : Palette.MawViolet);
        canvas.Text(barLeft + barWidth + (10 * scale), barTop + (1 * scale),
            string.Create(CultureInfo.InvariantCulture, $"INFESTATION {_campaign.Meter}%"), 1.2f * scale, meter >= 0.8f ? Palette.TextWarn : Palette.TextDim);
    }

    private void DrawPanel(Canvas canvas)
    {
        var scale = Scale;
        var left = _framebuffer.X - PanelWidth;
        canvas.Rect(left, 0, PanelWidth, _framebuffer.Y, Palette.Panel);
        canvas.Rect(left, 0, 2 * scale, _framebuffer.Y, Palette.PanelEdge);
        foreach (var widget in _widgets)
        {
            var hovered = widget.Enabled && widget.Contains(_mouse);
            var fill = !widget.Enabled ? Palette.Button : widget.Selected ? Palette.ButtonActive : hovered ? Palette.ButtonHover : Palette.PanelLight;
            canvas.Rect(widget.X, widget.Y, widget.W, widget.H, fill);
            canvas.Frame(widget.X, widget.Y, widget.W, widget.H, scale, widget.Enabled ? (widget.Primary ? Palette.Credits : Palette.PanelEdge) : Palette.Button);
            var textScale = PixelFont.Measure(widget.Text, 2 * scale) <= widget.W - (8 * scale) ? 2 * scale : 1.25f * scale;
            canvas.TextCentered(widget.X + (widget.W / 2), widget.Y + ((widget.H - (PixelFont.GlyphHeight * textScale)) / 2), widget.Text, textScale,
                widget.Enabled ? Palette.Text : Palette.TextDim);
        }

        foreach (var (x, y, text, size, color) in _labels)
        {
            canvas.Text(x, y, text, size, color);
        }
    }

    private void DrawOutcome(Canvas canvas)
    {
        var scale = Scale;
        var (left, top, fit) = MapTransform();
        var middle = left + (WorldMap.Width * fit / 2);
        var y = top + (WorldMap.Height * fit * 0.4f);
        var won = _campaign.Outcome == CampaignOutcome.Won;
        var title = won ? "THE WORLD IS TAKEN BACK" : "THE MAW HAS CONSUMED THE WORLD";
        canvas.Rect(left, y - (14 * scale), WorldMap.Width * fit, 70 * scale, new LinearColor(0, 0, 0, 0.65f));
        canvas.TextCentered(middle, y, title, 4 * scale, won ? Palette.TextGood : Palette.TextWarn);
        canvas.TextCentered(middle, y + (36 * scale), string.Create(CultureInfo.InvariantCulture, $"{_campaign.Turn} TURNS. PRESS ESC FOR THE MENU."), 1.5f * scale, Palette.Text);
    }

    private void Layout()
    {
        _widgets.Clear();
        _labels.Clear();
        var scale = Scale;
        var left = _framebuffer.X - PanelWidth + (16 * scale);
        var width = PanelWidth - (32 * scale);
        var y = 14 * scale;
        var small = 1.5f * scale;
        var line = 13 * scale;

        void Label(string text, float size, LinearColor color, float after = 0)
        {
            _labels.Add((left, y, text, size, color));
            y += (PixelFont.GlyphHeight * size) + (after > 0 ? after : 5 * scale);
        }

        void Paragraph(string text, LinearColor color, int maxLines = 12)
        {
            var lines = 0;
            foreach (var row in Wrap(text.ToUpperInvariant(), width, small))
            {
                if (lines++ >= maxLines)
                {
                    break;
                }

                _labels.Add((left, y, row, small, color));
                y += line;
            }

            y += 4 * scale;
        }

        if (!string.IsNullOrEmpty(Message))
        {
            Paragraph(Message, Palette.TextWarn);
        }

        // The selected region or event, and the mission it would be.
        if (_selected is { } target)
        {
            var campaignEvent = target.IsEvent ? _campaign.FindEvent(target.Id) : null;
            var region = _campaign.World.Regions[campaignEvent?.Region ?? target.Id];
            if (campaignEvent is not null)
            {
                Label(CampaignState.EventName(campaignEvent.Kind).ToUpperInvariant(), 2 * scale, Palette.Credits);
            }

            Label(region.Name.ToUpperInvariant(), 2.5f * scale, Palette.Text, 8 * scale);
            var owner = _campaign.OwnerOf(region.Id) switch
            {
                RegionOwner.Liberated => "LIBERATED",
                RegionOwner.Maw => string.Create(CultureInfo.InvariantCulture, $"HELD BY THE MAW, INFESTATION {_campaign.InfestationOf(region.Id)}"),
                _ => string.Create(CultureInfo.InvariantCulture, $"CONTESTED, INFESTATION {_campaign.InfestationOf(region.Id)}"),
            };
            Paragraph(string.Create(CultureInfo.InvariantCulture,
                $"{_campaign.World.Theatres[region.Theatre].Name}. {owner}{(_campaign.IsHive(region.Id) ? ". HIVE" : string.Empty)}{(region.IsHome ? ". HOME" : string.Empty)}."), Palette.TextDim);
            Paragraph(string.Create(CultureInfo.InvariantCulture,
                $"{CampaignState.Words(region.Terrain.ToString())}, {region.Biome}, richness {region.Richness}{(region.Features == RegionFeatures.None ? string.Empty : ". " + CampaignState.Words(region.Features.ToString()))}."), Palette.TextDim);
            if (campaignEvent is not null)
            {
                var turns = campaignEvent.ExpiresAfterTurn - _campaign.Turn;
                Paragraph(string.Create(CultureInfo.InvariantCulture, $"{turns} TURN{(turns == 1 ? string.Empty : "S")} TO RESPOND. IGNORED: {Consequence(campaignEvent.Kind)}"), Palette.TextWarn);
            }

            if (_campaign.CanTarget(target, out var reason))
            {
                var mission = _campaign.PlanMission(target, _difficulty);
                Paragraph("MISSION: " + mission.Briefing, Palette.Text);
                Paragraph(string.Create(CultureInfo.InvariantCulture,
                    $"YOU AND {Allies} AI ALL{(Allies == 1 ? "Y" : "IES")}. A WIN LIBERATES; A LOSS RAISES THE INFESTATION. EITHER WAY THE MAW THEN MOVES."), Palette.TextDim);
                _widgets.Add(new Widget(left, y, width, 34 * scale, "LAUNCH MISSION", () => Choice = new CampaignChoice(CampaignChoiceKind.Launch, target, _difficulty), true) { Primary = true });
                y += 44 * scale;
            }
            else
            {
                Paragraph(reason == "the region does not border liberated territory" ? "NOT ON THE FRONT: LIBERATE A NEIGHBOURING REGION FIRST." : reason.ToUpperInvariant() + ".", Palette.TextDim);
            }
        }
        else
        {
            Paragraph("SELECT A REGION ON THE MAP.", Palette.TextDim);
        }

        // Events waiting for an answer.
        if (_campaign.Events.Count > 0)
        {
            y += 4 * scale;
            Label("EVENTS", 2 * scale, Palette.Credits);
            foreach (var campaignEvent in _campaign.Events)
            {
                var id = campaignEvent.Id;
                var turns = campaignEvent.ExpiresAfterTurn - _campaign.Turn;
                var text = string.Create(CultureInfo.InvariantCulture,
                    $"{CampaignState.EventName(campaignEvent.Kind).ToUpperInvariant()} - {_campaign.World.Regions[campaignEvent.Region].Name.ToUpperInvariant()} ({turns})");
                _widgets.Add(new Widget(left, y, width, 24 * scale, text, () => _selected = CampaignTarget.Event(id), true)
                {
                    Selected = _selected is { IsEvent: true } chosen && chosen.Id == id,
                });
                y += 28 * scale;
            }
        }

        // What the last turn did.
        if (LastTurn is { } report && report.Lines.Count > 0)
        {
            y += 4 * scale;
            Label(string.Create(CultureInfo.InvariantCulture, $"TURN {report.Turn}: {(report.Won ? "VICTORY" : "DEFEAT")}"), 2 * scale, report.Won ? Palette.TextGood : Palette.TextWarn);
            var room = Math.Max(2, (int)((_framebuffer.Y - (110 * scale) - y) / line));
            Paragraph(string.Join(" ", report.Lines), Palette.TextDim, room);
        }

        // Bottom: lower the difficulty (never raise it) and leave.
        var bottom = _framebuffer.Y - (16 * scale) - (34 * scale);
        var half = (width - (8 * scale)) / 2;
        var lowered = _difficulty < _campaign.Difficulty;
        _labels.Add((left, bottom - (18 * scale), string.Create(CultureInfo.InvariantCulture,
            $"THE MAW: {_difficulty.ToString().ToUpperInvariant()}{(lowered ? " (LOWERED FROM " + _campaign.Difficulty.ToString().ToUpperInvariant() + ")" : string.Empty)}"), small, lowered ? Palette.TextWarn : Palette.TextDim));
        _widgets.Add(new Widget(left, bottom, half, 34 * scale, "EASIER", () => _difficulty--, _difficulty > MawDifficulty.Recruit && _campaign.Outcome == CampaignOutcome.InProgress));
        _widgets.Add(new Widget(left + half + (8 * scale), bottom, half, 34 * scale, "SAVE AND EXIT", Leave, true));
    }

    private void Leave() => Choice = new CampaignChoice(CampaignChoiceKind.Leave, default, _difficulty);

    private static string Consequence(CampaignEventKind kind) => kind switch
    {
        CampaignEventKind.MeteorStrike => "NESTS TAKE ROOT IN THE CRATER (INFESTATION 3).",
        CampaignEventKind.BehemothMigration => "THE COLUMN REINFESTS A LIBERATED NEIGHBOUR.",
        CampaignEventKind.OutpostSiege => "THE REGION IS LOST.",
        CampaignEventKind.HiveEmergence => "A NEW HIVE APPEARS.",
        CampaignEventKind.SurvivorConvoy => "THE SURVIVORS AND THEIR RESEARCH ARE LOST.",
        _ => "IT EXPIRES.",
    };

    private static IEnumerable<string> Wrap(string text, float width, float scale)
    {
        var max = Math.Max(8, (int)(width / (PixelFont.Advance * scale)));
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > max)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    private sealed record Widget(float X, float Y, float W, float H, string Text, Action? Click, bool Enabled)
    {
        public bool Primary { get; init; }

        public bool Selected { get; init; }

        public bool Contains(Vector2 point) => point.X >= X && point.X < X + W && point.Y >= Y && point.Y < Y + H;
    }
}
