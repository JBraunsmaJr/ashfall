using System.Globalization;
using System.Numerics;
using Ashfall.MapGen;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.UI.Drawing;
using Ashfall.UI.Input;
using Ashfall.UI.Text;

using Ashfall.Campaign;
using Ashfall.Simulation.Horde;

namespace Ashfall.UI.Menu;

/// <summary>
/// The title screen: continue or start the co-op campaign, start a Purge against the Maw or a skirmish, host a lobby or join one by
/// address, without any command-line flags. Purge, Skirmish and Host also pick the map: a generated
/// archetype with its seed and sliders (or, outside a Purge, the hand-made map), previewed live as
/// they change. Each page is laid out fresh every frame from the same widget
/// list that handles clicks, so what is drawn is exactly what can be clicked. Tab moves between text
/// boxes, Enter confirms, Esc goes back.
/// </summary>
public sealed class MainMenuScreen
{
    private const int MaxPlayers = 4;

    private readonly List<Widget> _widgets = [];
    private readonly List<(float X, float Y, string Text, float Size, LinearColor Color, bool Centered)> _labels = [];
    private readonly List<(float X, float Y, float Size)> _previews = [];
    private readonly TextField _name = new("NAME", 16, TextField.IsNameChar);
    private readonly TextField _seed = new("SEED", 19, char.IsAsciiDigit);
    private readonly TextField _port = new("PORT", 5, char.IsAsciiDigit);
    private readonly TextField _address = new("ADDRESS", 64, TextField.IsAddressChar);
    private readonly TextField _mapSeed = new("MAP SEED", 10, char.IsAsciiDigit);
    private readonly TextField _mapCode = new("CODE", 28, c => char.IsAsciiLetterOrDigit(c) || c == '-');
    private readonly TextField _campaignSeed = new("WORLD SEED", 10, char.IsAsciiDigit);
    private readonly IReadOnlyList<string> _recentHosts;
    private readonly int[] _sliders = new int[MapSettings.SliderCount];
    private MapArchetype? _archetype;
    private Page _page = Page.Main;
    private bool _idleOpponent;
    private int _humans;
    private int _ais;
    private int _allies;
    private int _nests;
    private MawDifficulty _difficulty;
    private CampaignRules _campaignRules;
    private MawDifficulty _campaignDifficulty;
    private int _campaignAllies;
    private MapSlider? _dragging;
    private Vector2 _framebuffer = new(1280, 720);
    private Vector2 _mouse;
    private double _time;
    private float _contentBottom;

    public MainMenuScreen(MenuSettings settings, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _name.Text = settings.Name;
        _seed.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);
        _port.Text = settings.Port.ToString(CultureInfo.InvariantCulture);
        _address.Text = settings.RecentHosts.Count > 0 ? settings.RecentHosts[0] : string.Empty;
        _recentHosts = settings.RecentHosts;
        _idleOpponent = settings.IdleOpponent;
        _humans = settings.Humans;
        _ais = settings.Ais;
        _allies = settings.PurgeAllies;
        _nests = settings.PurgeNests;
        _difficulty = settings.PurgeDifficulty;
        _campaignRules = CampaignRuleSets.Find(settings.CampaignRules) ?? CampaignRuleSets.Standard;
        _campaignDifficulty = settings.CampaignDifficulty;
        _campaignAllies = settings.CampaignAllies;
        _campaignSeed.Text = settings.CampaignSeed.ToString(CultureInfo.InvariantCulture);
        SetMap(settings.Map);
        Message = message;
    }

    private enum Page
    {
        Main,
        Campaign,
        Purge,
        Skirmish,
        Host,
        Join,
    }

    /// <summary>Set once the player confirms a page (or quits); the client acts on it.</summary>
    public MenuChoice? Choice { get; private set; }

    /// <summary>A line shown under the main buttons, e.g. why the last connection failed.</summary>
    public string? Message { get; set; }

    /// <summary>One line describing the saved campaign (offered as CONTINUE), or null if there is none.</summary>
    public string? SavedCampaign { get; init; }

    /// <summary>The page on screen: Main, Campaign, Purge, Skirmish, Host or Join.</summary>
    public string CurrentPage => _page.ToString();

    /// <summary>The live map preview on the Skirmish and Host pages.</summary>
    public MapPreview Preview { get; init; } = new();

    /// <summary>The generated map as currently set up, or null for the hand-made map.</summary>
    public MapSettings? Map => _archetype is null
        ? null
        : new MapSettings(_archetype, uint.TryParse(_mapSeed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ? seed : 1, _sliders);

    /// <summary>The settings as currently entered (what gets remembered for next time).</summary>
    public MenuSettings Settings => new()
    {
        Name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "PLAYER",
        Seed = ulong.TryParse(_seed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ? seed : Ashfall.Session.ProvingGround.DefaultSeed,
        IdleOpponent = _idleOpponent,
        Humans = _humans,
        Ais = _ais,
        Port = ParsePort(_port.Text) ?? HostAddress.DefaultPort,
        PurgeAllies = _allies,
        PurgeNests = _nests,
        PurgeDifficulty = _difficulty,
        CampaignRules = _campaignRules.Id,
        CampaignDifficulty = _campaignDifficulty,
        CampaignSeed = CampaignSeed ?? 1,
        CampaignAllies = _campaignAllies,
        Map = Map,
        RecentHosts = _recentHosts,
    };

    /// <summary>Every clickable thing on the current page with its label, centre and size, for tests and automation.</summary>
    public IEnumerable<(string Label, Vector2 Center, Vector2 Size, bool Enabled)> Targets
    {
        get
        {
            Layout();
            return _widgets.Select(w => (w.Field?.Label ?? w.Text, new Vector2(w.X + (w.W / 2), w.Y + (w.H / 2)), new Vector2(w.W, w.H), w.Enabled)).ToList();
        }
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
        var fields = _widgets.Where(w => w.Field is not null).Select(w => w.Field!).ToList();
        if (input.WasPressed(Key.Escape))
        {
            if (_page != Page.Main)
            {
                Go(Page.Main);
            }

            return;
        }

        if (input.WasPressed(Key.Tab) && fields.Count > 0)
        {
            var current = fields.FindIndex(f => f.Focused);
            Focus(fields[(current + 1) % fields.Count]);
        }

        foreach (var field in fields)
        {
            if (field.Edit(input) && field == _mapCode && MapSettings.TryParseCode(_mapCode.Text, out var pasted))
            {
                SetMap(pasted);
                _mapCode.Focused = true;
            }
        }

        // Dragging a slider bar: the value follows the mouse until the button comes up.
        if (_dragging is { } dragging)
        {
            if (input.IsDown(MouseButton.Left) && _widgets.FirstOrDefault(w => w.Bar == dragging) is { } bar)
            {
                SetSlider(dragging, (int)MathF.Round(Math.Clamp((input.Mouse.X - bar.X) / bar.W, 0f, 1f) * 100f));
            }
            else
            {
                _dragging = null;
            }
        }

        if (input.WasPressed(Key.Enter))
        {
            var primary = _widgets.FirstOrDefault(w => w.Primary);
            if (primary is { Enabled: true, Click: { } confirm })
            {
                confirm();
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
                if (widget.Field is { } field)
                {
                    Focus(field);
                }
                else if (widget.Bar is { } slider)
                {
                    _dragging = slider;
                    SetSlider(slider, (int)MathF.Round(Math.Clamp((input.Mouse.X - widget.X) / widget.W, 0f, 1f) * 100f));
                }
                else if (widget.Enabled)
                {
                    widget.Click?.Invoke();
                }

                return;
            }
        }

        Focus(null);
    }

    public void Draw(FrameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.Clear();
        scene.ClearColor = Palette.Clear;
        Layout();
        var canvas = new Canvas(scene);
        var scale = Scale;
        DrawBackdrop(canvas);

        var (left, top, width, height) = PanelRect();
        canvas.Rect(left, top, width, height, Palette.Panel);
        canvas.Frame(left, top, width, height, 2 * scale, Palette.PanelEdge);

        foreach (var widget in _widgets)
        {
            var hovered = widget.Enabled && widget.Contains(_mouse);
            if (widget.Field is { } field)
            {
                canvas.Rect(widget.X, widget.Y, widget.W, widget.H, Palette.Clear);
                canvas.Frame(widget.X, widget.Y, widget.W, widget.H, scale, field.Focused ? Palette.Credits : hovered ? Palette.PanelEdge : Palette.Button);
                var caret = field.Focused && ((int)(_time * 2) % 2 == 0) ? "_" : string.Empty;
                var text = FitTail(field.Text, widget.W - (16 * scale), 2 * scale) + caret;
                canvas.Text(widget.X + (8 * scale), widget.Y + ((widget.H - (PixelFont.GlyphHeight * 2 * scale)) / 2), text, 2 * scale, Palette.Text);
                continue;
            }

            if (widget.Bar is { } slider)
            {
                // Track, fill to the value, and a handle.
                var value = _sliders[(int)slider] / 100f;
                var trackY = widget.Y + (widget.H / 2) - (3 * scale);
                canvas.Rect(widget.X, trackY, widget.W, 6 * scale, Palette.Button);
                canvas.Rect(widget.X, trackY, widget.W * value, 6 * scale, Palette.Scale(Palette.Cindrite, hovered || _dragging == slider ? 0.9f : 0.7f));
                canvas.Rect(widget.X + (widget.W * value) - (3 * scale), widget.Y + (2 * scale), 6 * scale, widget.H - (4 * scale), Palette.Text);
                continue;
            }

            var fill = !widget.Enabled ? Palette.Button : widget.Selected ? Palette.ButtonActive : hovered ? Palette.ButtonHover : Palette.PanelLight;
            canvas.Rect(widget.X, widget.Y, widget.W, widget.H, fill);
            canvas.Frame(widget.X, widget.Y, widget.W, widget.H, scale, widget.Enabled ? (widget.Primary ? Palette.Credits : Palette.PanelEdge) : Palette.Button);
            // Labels too wide for their button drop to a smaller size rather than spill out.
            var textScale = PixelFont.Measure(widget.Text, 2 * scale) <= widget.W - (8 * scale) ? 2 * scale : 1.5f * scale;
            canvas.TextCentered(widget.X + (widget.W / 2), widget.Y + ((widget.H - (PixelFont.GlyphHeight * textScale)) / 2), widget.Text, textScale,
                widget.Enabled ? Palette.Text : Palette.TextDim);
        }

        foreach (var (x, y, size) in _previews)
        {
            Preview.Draw(canvas, x, y, size, 1.5f * scale);
        }

        foreach (var (x, y, text, size, color, centered) in _labels)
        {
            if (centered)
            {
                canvas.TextCentered(x, y, text, size, color);
            }
            else
            {
                canvas.Text(x, y, text, size, color);
            }
        }
    }

    private float Scale => _framebuffer.Y >= 1300 ? 2f : _framebuffer.Y >= 960 ? 1.5f : 1f;

    private bool Wide => _page is Page.Purge or Page.Skirmish or Page.Host;

    private (float Left, float Top, float Width, float Height) PanelRect()
    {
        var scale = Scale;
        var width = Math.Min(_framebuffer.X - (40 * scale), (Wide ? 920 : 560) * scale);
        var top = Wide ? 70 * scale : Math.Max(110 * scale, _framebuffer.Y * 0.2f);
        return ((_framebuffer.X - width) / 2f, top, width, Math.Max(100 * scale, _contentBottom - top + (8 * scale)));
    }

    // A few drifting embers behind the panel, so the title screen isn't a flat void.
    private void DrawBackdrop(Canvas canvas)
    {
        var scale = Scale;
        var titleScale = (Wide ? 6 : 9) * scale;
        canvas.TextCentered(_framebuffer.X / 2, Math.Max(8 * scale, PanelRect().Top - (PixelFont.GlyphHeight * titleScale) - (10 * scale)), "ASHFALL", titleScale, Palette.Cindrite);
        for (var i = 0; i < 40; i++)
        {
            var phase = (float)((_time * (0.02 + ((i % 7) * 0.006))) + (i * 0.618)) % 1f;
            var x = ((i * 0.37f) % 1f) * _framebuffer.X;
            var y = _framebuffer.Y * (1f - phase);
            var size = (2 + (i % 3)) * scale;
            canvas.Rect(x + (MathF.Sin((float)_time + i) * 12 * scale), y, size, size, Palette.WithAlpha(i % 2 == 0 ? Palette.Cindrite : Palette.CindriteHot, 0.25f + (0.5f * (1f - phase))));
        }
    }

    private void Layout()
    {
        _widgets.Clear();
        _labels.Clear();
        _previews.Clear();
        var scale = Scale;
        var (left, top, width, _) = PanelRect();
        var center = left + (width / 2);
        var y = top + (20 * scale);
        var inner = left + (24 * scale);
        var innerWidth = width - (48 * scale);
        var rowHeight = 30 * scale;
        var buttonHeight = 34 * scale;

        // Setup pages have two columns: the player's settings and the preview on the left, the map on the right.
        var columnWidth = Wide ? (innerWidth - (24 * scale)) / 2 : innerWidth;
        var x0 = inner;

        void Heading(string text)
        {
            _labels.Add((center, y, text, 3 * scale, Palette.Credits, true));
            y += 40 * scale;
        }

        void Note(string text, LinearColor color)
        {
            foreach (var line in Wrap(text, columnWidth, 1.5f * scale))
            {
                _labels.Add((x0, y, line, 1.5f * scale, color, false));
                y += 13 * scale;
            }

            y += 3 * scale;
        }

        void FieldRow(TextField field, float labelWidth = 130)
        {
            _labels.Add((x0, y + (8 * scale), field.Label, 2 * scale, Palette.TextDim, false));
            _widgets.Add(new Widget(x0 + (labelWidth * scale), y, columnWidth - (labelWidth * scale), rowHeight, string.Empty, null, true) { Field = field });
            y += rowHeight + (10 * scale);
        }

        void Stepper(string label, int value, Action down, bool canDown, Action up, bool canUp)
        {
            _labels.Add((x0, y + (8 * scale), label, 2 * scale, Palette.TextDim, false));
            var x = x0 + (130 * scale);
            _widgets.Add(new Widget(x, y, 30 * scale, rowHeight, "-", down, canDown));
            _labels.Add((x + (60 * scale), y + (8 * scale), value.ToString(CultureInfo.InvariantCulture), 2 * scale, Palette.Text, true));
            _widgets.Add(new Widget(x + (90 * scale), y, 30 * scale, rowHeight, "+", up, canUp));
            y += rowHeight + (10 * scale);
        }

        void Choices(string label, IReadOnlyList<(string Text, bool Selected, Action Click)> options, float labelWidth = 130)
        {
            _labels.Add((x0, y + (8 * scale), label, 2 * scale, Palette.TextDim, false));
            var available = columnWidth - (labelWidth * scale);
            var gap = 8 * scale;
            var each = (available - (gap * (options.Count - 1))) / options.Count;
            for (var i = 0; i < options.Count; i++)
            {
                var (text, selected, click) = options[i];
                _widgets.Add(new Widget(x0 + (labelWidth * scale) + (i * (each + gap)), y, each, rowHeight, text, click, true) { Selected = selected });
            }

            y += rowHeight + (10 * scale);
        }

        void Actions(string primary, Action confirm, bool enabled)
        {
            y += 10 * scale;
            var half = Math.Min((innerWidth - (16 * scale)) / 2, 260 * scale);
            var right = inner + innerWidth;
            _widgets.Add(new Widget(right - (2 * half) - (16 * scale), y, half, buttonHeight, "BACK", () => Go(Page.Main), true));
            _widgets.Add(new Widget(right - half, y, half, buttonHeight, primary, confirm, enabled) { Primary = true });
            y += buttonHeight + (14 * scale);
        }

        // The map column: archetype, seed, sliders, code, limits. Returns the bottom of the column.
        float MapColumn(float columnTop, int players, bool handMade = true)
        {
            x0 = inner + columnWidth + (24 * scale);
            y = columnTop;
            var options = new List<(string, bool, Action)>();
            if (handMade)
            {
                options.Add(("HAND-MADE", _archetype is null, () => SetMap(null)));
            }

            foreach (var archetype in MapArchetypes.All)
            {
                var target = archetype;
                options.Add((archetype.Name.ToUpperInvariant(), _archetype == archetype, () => SetMap(new MapSettings(target, MapSeedOrRandom()))));
            }

            Choices("MAP", options, 70);
            if (_archetype is null)
            {
                Note(players <= 2
                    ? "THE HAND-MADE DUEL MAP: A RIDGE WITH THREE GAPS, A FIELD PER BASE AND A RICH CENTRE."
                    : "THE HAND-MADE QUAD MAP: FOUR CORNERS SPLIT BY WALLS, A RICH OPEN CENTRE.", Palette.TextDim);
                return y;
            }

            Note(_archetype.Description.ToUpperInvariant(), Palette.TextDim);
            _labels.Add((x0, y + (8 * scale), _mapSeed.Label, 2 * scale, Palette.TextDim, false));
            _widgets.Add(new Widget(x0 + (110 * scale), y, columnWidth - (230 * scale), rowHeight, string.Empty, null, true) { Field = _mapSeed });
            _widgets.Add(new Widget(x0 + columnWidth - (110 * scale), y, 110 * scale, rowHeight, "NEW MAP", NewMapSeed, true));
            y += rowHeight + (10 * scale);

            foreach (var slider in Enum.GetValues<MapSlider>())
            {
                var rule = _archetype.Rule(slider);
                var value = _sliders[(int)slider];
                var limited = value < rule.Min || value > rule.Max;
                _labels.Add((x0, y + (6 * scale), SliderName(slider), 2 * scale, Palette.TextDim, false));
                _widgets.Add(new Widget(x0 + (130 * scale), y, columnWidth - (180 * scale), 22 * scale, SliderName(slider), null, true) { Bar = slider });
                _labels.Add((x0 + columnWidth - (40 * scale), y + (6 * scale), value.ToString(CultureInfo.InvariantCulture), 2 * scale, limited ? Palette.TextWarn : Palette.Text, false));
                y += 28 * scale;
            }

            y += 6 * scale;
            if (!_mapCode.Focused)
            {
                _mapCode.Text = Map!.ToCode();
            }

            FieldRow(_mapCode, 70);
            if (Preview.Map?.Generated is { } generated && generated.Requested.Equals(Map))
            {
                foreach (var clamp in generated.Clamps)
                {
                    Note(clamp.ToUpperInvariant(), Palette.TextWarn);
                }
            }

            return y;
        }

        // The preview square in the left column, with a one-line summary under it.
        void PreviewBlock(int players, float bottomLimit, bool coop = false)
        {
            Preview.Request(Map, players, coop);
            var size = Math.Max(120 * scale, Math.Min(columnWidth, bottomLimit - y - (20 * scale)));
            _previews.Add((x0, y, size));
            y += size + (6 * scale);
            var map = Preview.Map;
            var summary = map is null ? "GENERATING..."
                : map.Generated is { } generated
                    ? string.Create(CultureInfo.InvariantCulture, $"{map.Grid.Width}X{map.Grid.Height}, {players} PLAYERS, FAIRNESS {generated.Report.FairnessScore}{(generated.FellBack ? ", PLAIN FALLBACK" : string.Empty)}")
                    : string.Create(CultureInfo.InvariantCulture, $"{map.Name.ToUpperInvariant()} MAP, {map.Grid.Width}X{map.Grid.Height}");
            _labels.Add((x0, y, summary, 1.5f * scale, Palette.TextDim, false));
            y += 16 * scale;
        }

        var bottom = _framebuffer.Y - (70 * scale);
        switch (_page)
        {
            case Page.Main:
            {
                _labels.Add((center, y, "BULWARK DIRECTORATE - TAKE THE WORLD BACK FROM THE MAW", 1.5f * scale, Palette.TextDim, true));
                y += 34 * scale;
                var buttonWidth = Math.Min(innerWidth, 300 * scale);
                foreach (var (label, page) in new[] { ("CAMPAIGN", Page.Campaign), ("PURGE", Page.Purge), ("SKIRMISH", Page.Skirmish), ("HOST GAME", Page.Host), ("JOIN GAME", Page.Join) })
                {
                    var target = page;
                    _widgets.Add(new Widget(center - (buttonWidth / 2), y, buttonWidth, buttonHeight + (4 * scale), label, () => Go(target), true) { Primary = page == Page.Campaign });
                    y += buttonHeight + (16 * scale);
                }

                _widgets.Add(new Widget(center - (buttonWidth / 2), y, buttonWidth, buttonHeight + (4 * scale), "QUIT", () => Choose(MenuAction.Quit), true));
                y += buttonHeight + (24 * scale);
                if (!string.IsNullOrEmpty(Message))
                {
                    foreach (var line in Wrap(Message.ToUpperInvariant(), innerWidth, 1.5f * scale))
                    {
                        _labels.Add((center, y, line, 1.5f * scale, Palette.TextWarn, true));
                        y += 14 * scale;
                    }
                }

                break;
            }

            case Page.Campaign:
            {
                Heading("CAMPAIGN");
                if (SavedCampaign is not null)
                {
                    Note("SAVED: " + SavedCampaign.ToUpperInvariant(), Palette.Text);
                    _widgets.Add(new Widget(x0, y, columnWidth, buttonHeight, "CONTINUE", () => Choice = new MenuChoice(MenuAction.Campaign, Settings), true) { Primary = true });
                    y += buttonHeight + (18 * scale);
                }

                _labels.Add((x0, y, "NEW CAMPAIGN", 2 * scale, Palette.Credits, false));
                y += 24 * scale;
                Choices("RULES", CampaignRuleSets.All.Select(rules =>
                {
                    var target = rules;
                    return (rules.Name.ToUpperInvariant(), _campaignRules == rules, (Action)(() => _campaignRules = target));
                }).ToList());
                Choices("THE MAW", [(_campaignDifficulty.ToString().ToUpperInvariant(), true, () => _campaignDifficulty = (MawDifficulty)(((int)_campaignDifficulty + 1) % 4))]);
                _labels.Add((x0, y + (8 * scale), _campaignSeed.Label, 2 * scale, Palette.TextDim, false));
                _widgets.Add(new Widget(x0 + (130 * scale), y, columnWidth - (250 * scale), rowHeight, string.Empty, null, true) { Field = _campaignSeed });
                _widgets.Add(new Widget(x0 + columnWidth - (110 * scale), y, 110 * scale, rowHeight, "RANDOM", RandomCampaignSeed, true));
                y += rowHeight + (10 * scale);
                Stepper("AI ALLIES", _campaignAllies, () => _campaignAllies--, _campaignAllies > 0, () => _campaignAllies++, _campaignAllies + 1 < Ashfall.Session.MatchSpec.PurgeMaxPlayers);
                var seed = CampaignSeed;
                Note(seed is { } s
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"CODE {CampaignCode.For(s, _campaignRules, _campaignDifficulty)}. {_campaignRules.MinRegions}-{_campaignRules.MaxRegions} REGIONS IN {_campaignRules.MinTheatres}-{_campaignRules.MaxTheatres} THEATRES, ONE HIVE EACH. CHOOSE WHERE TO FIGHT; EVERY MISSION, WON OR LOST, IS A TURN. THE MAW SPREADS BETWEEN TURNS; DESTROY EVERY HIVE BEFORE THE INFESTATION METER FILLS.")
                    : "ENTER A NUMBER FOR THE WORLD SEED.", seed is null ? Palette.TextWarn : Palette.TextDim);
                if (SavedCampaign is not null)
                {
                    Note("STARTING A NEW CAMPAIGN REPLACES THE SAVED ONE.", Palette.TextWarn);
                }

                y += 10 * scale;
                var half = Math.Min((innerWidth - (16 * scale)) / 2, 260 * scale);
                var right = inner + innerWidth;
                _widgets.Add(new Widget(right - (2 * half) - (16 * scale), y, half, buttonHeight, "BACK", () => Go(Page.Main), true));
                _widgets.Add(new Widget(right - half, y, half, buttonHeight, "NEW CAMPAIGN", () => Choice = new MenuChoice(MenuAction.Campaign, Settings) { NewCampaign = true }, seed is not null)
                {
                    Primary = SavedCampaign is null,
                });
                y += buttonHeight + (14 * scale);
                break;
            }

            case Page.Purge:
            {
                Heading("PURGE");
                var columnTop = y;
                var players = 1 + _allies;
                Stepper("AI ALLIES", _allies, () => _allies--, _allies > 0, () => _allies++, players < Ashfall.Session.MatchSpec.PurgeMaxPlayers);
                Stepper("NESTS", _nests, () => _nests--, _nests > 1, () => _nests++, _nests < Ashfall.Session.MatchSpec.PurgeMaxNests);
                Choices("THE MAW", [(_difficulty.ToString().ToUpperInvariant(), true, () => _difficulty = (MawDifficulty)(((int)_difficulty + 1) % 4))]);
                _labels.Add((x0, y + (8 * scale), _seed.Label, 2 * scale, Palette.TextDim, false));
                _widgets.Add(new Widget(x0 + (130 * scale), y, columnWidth - (250 * scale), rowHeight, string.Empty, null, true) { Field = _seed });
                _widgets.Add(new Widget(x0 + columnWidth - (110 * scale), y, 110 * scale, rowHeight, "RANDOM", RandomSeed, true));
                y += rowHeight + (10 * scale);
                var seedOk = ulong.TryParse(_seed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _);
                Note(seedOk
                    ? "CO-OP AGAINST THE MAW: DESTROY EVERY NEST. NESTS FEED ON THE CINDRITE FIELDS AND BREED; FROM THREE MINUTES THE MAW PROBES, RAIDS AND ASSAULTS, ALWAYS WITH WARNING. CLICK THE MAW TO CHANGE ITS DIFFICULTY. AI ALLIES SHARE YOUR TEAM."
                    : "ENTER A NUMBER FOR THE SEED.", seedOk ? Palette.TextDim : Palette.TextWarn);
                PreviewBlock(players, bottom, coop: true);
                var leftBottom = y;
                var rightBottom = MapColumn(columnTop, players, handMade: false);
                x0 = inner;
                y = Math.Max(leftBottom, rightBottom);
                Actions("START", () => Choose(MenuAction.Purge), seedOk && MapOk());
                break;
            }

            case Page.Skirmish:
            {
                Heading("SKIRMISH");
                var columnTop = y;
                Choices("OPPONENT", [("SCRIPTED AI", !_idleOpponent, () => _idleOpponent = false), ("IDLE", _idleOpponent, () => _idleOpponent = true)]);
                _labels.Add((x0, y + (8 * scale), _seed.Label, 2 * scale, Palette.TextDim, false));
                _widgets.Add(new Widget(x0 + (130 * scale), y, columnWidth - (250 * scale), rowHeight, string.Empty, null, true) { Field = _seed });
                _widgets.Add(new Widget(x0 + columnWidth - (110 * scale), y, 110 * scale, rowHeight, "RANDOM", RandomSeed, true));
                y += rowHeight + (10 * scale);
                var seedOk = ulong.TryParse(_seed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _);
                Note(seedOk ? "ONE VERSUS ONE. IDLE NEVER ACTS: USE IT TO LEARN THE CONTROLS." : "ENTER A NUMBER FOR THE SEED.", seedOk ? Palette.TextDim : Palette.TextWarn);
                PreviewBlock(2, bottom);
                var leftBottom = y;
                var rightBottom = MapColumn(columnTop, 2);
                x0 = inner;
                y = Math.Max(leftBottom, rightBottom);
                Actions("START", () => Choose(MenuAction.Skirmish), seedOk && MapOk());
                break;
            }

            case Page.Host:
            {
                Heading("HOST GAME");
                var columnTop = y;
                FieldRow(_name);
                var total = _humans + _ais;
                Stepper("PLAYERS", _humans, () => _humans--, _humans > 1 && total > 2, () => _humans++, total < MaxPlayers);
                Stepper("AI", _ais, () => _ais--, _ais > 0 && total > 2, () => _ais++, total < MaxPlayers);
                FieldRow(_port);
                var port = ParsePort(_port.Text);
                Note(port is { } p
                    ? string.Create(CultureInfo.InvariantCulture, $"PLAYERS COUNTS YOU. FOR INTERNET PLAY, FORWARD UDP PORT {p} TO THIS PC; FRIENDS JOIN WITH YOUR PUBLIC IP.")
                    : "PORT MUST BE A NUMBER FROM 1 TO 65535.", port is null ? Palette.TextWarn : Palette.TextDim);
                var nameOk = _name.Text.Trim().Length > 0;
                if (!nameOk)
                {
                    Note("ENTER A NAME.", Palette.TextWarn);
                }

                PreviewBlock(total, bottom);
                var leftBottom = y;
                var rightBottom = MapColumn(columnTop, total);
                x0 = inner;
                y = Math.Max(leftBottom, rightBottom);
                Actions("HOST", () => Choose(MenuAction.Host), nameOk && port is not null && MapOk());
                break;
            }

            case Page.Join:
            {
                Heading("JOIN GAME");
                FieldRow(_name);
                FieldRow(_address);
                var addressOk = HostAddress.TryParse(_address.Text, out _, out _);
                Note(addressOk || _address.Text.Length == 0
                    ? "THE HOST'S ADDRESS, E.G. 203.0.113.7 OR 192.168.1.20:47600 (PORT DEFAULTS TO 47600). THE HOST CHOOSES THE MAP."
                    : "THAT DOES NOT LOOK LIKE AN ADDRESS: USE HOST OR HOST:PORT.", addressOk || _address.Text.Length == 0 ? Palette.TextDim : Palette.TextWarn);
                if (_recentHosts.Count > 0)
                {
                    y += 4 * scale;
                    _labels.Add((inner, y, "RECENT", 1.5f * scale, Palette.TextDim, false));
                    y += 14 * scale;
                    foreach (var host in _recentHosts)
                    {
                        var target = host;
                        _widgets.Add(new Widget(inner, y, innerWidth, 22 * scale, host.ToUpperInvariant(), () => _address.Text = target, true)
                        {
                            Selected = string.Equals(host, _address.Text, StringComparison.OrdinalIgnoreCase),
                        });
                        y += 26 * scale;
                    }
                }

                var nameOk = _name.Text.Trim().Length > 0;
                if (!nameOk)
                {
                    Note("ENTER A NAME.", Palette.TextWarn);
                }

                Actions("JOIN", Join, addressOk && nameOk);
                break;
            }
        }

        _contentBottom = y;
    }

    private uint? CampaignSeed => uint.TryParse(_campaignSeed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ? seed : null;

    private void RandomCampaignSeed() =>
        _campaignSeed.Text = ((uint)Random.Shared.Next(1, int.MaxValue)).ToString(CultureInfo.InvariantCulture);

    private bool MapOk() => _archetype is null || uint.TryParse(_mapSeed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private void SetMap(MapSettings? map)
    {
        _archetype = map?.Archetype;
        if (map is null)
        {
            return;
        }

        _mapSeed.Text = map.Seed.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < _sliders.Length; i++)
        {
            _sliders[i] = map.Sliders[i];
        }
    }

    private void SetSlider(MapSlider slider, int value) => _sliders[(int)slider] = Math.Clamp(value, 0, 100);

    private uint MapSeedOrRandom() =>
        uint.TryParse(_mapSeed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ? seed : (uint)Random.Shared.Next(1, int.MaxValue);

    private void NewMapSeed() => _mapSeed.Text = ((uint)Random.Shared.Next(1, int.MaxValue)).ToString(CultureInfo.InvariantCulture);

    private static string SliderName(MapSlider slider) => slider switch
    {
        MapSlider.BaseSpacing => "SPACING",
        MapSlider.Chokepoints => "CHOKES",
        MapSlider.Vegetation => "TREES",
        _ => slider.ToString().ToUpperInvariant(),
    };

    private void Go(Page page)
    {
        _page = page;
        Message = null;
        _dragging = null;
        Focus(null);
        if (page == Page.Purge && _archetype is null)
        {
            SetMap(new MapSettings(MapArchetypes.Crossroads, MapSeedOrRandom())); // a Purge needs a generated map
        }

        if (page == Page.Join)
        {
            Focus(_address);
        }
    }

    private void Focus(TextField? field)
    {
        foreach (var f in new[] { _name, _seed, _port, _address, _mapSeed, _mapCode, _campaignSeed })
        {
            f.Focused = f == field;
        }
    }

    private void RandomSeed() =>
        _seed.Text = ((ulong)Random.Shared.NextInt64(1, 1_000_000_000)).ToString(CultureInfo.InvariantCulture);

    private void Join()
    {
        if (!HostAddress.TryParse(_address.Text, out var host, out var port))
        {
            return;
        }

        var settings = Settings.WithRecentHost(HostAddress.Format(host, port));
        Choice = new MenuChoice(MenuAction.Join, settings) { JoinHost = host, JoinPort = port };
    }

    private void Choose(MenuAction action) => Choice = new MenuChoice(action, Settings);

    private static int? ParsePort(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535 ? port : null;

    // Long text in a box shows its end, where the caret is.
    private static string FitTail(string text, float width, float scale)
    {
        var max = Math.Max(1, (int)(width / (PixelFont.Advance * scale)) - 1);
        return text.Length <= max ? text : text[^max..];
    }

    private static IEnumerable<string> Wrap(string text, float width, float scale)
    {
        var max = Math.Max(8, (int)(width / (PixelFont.Advance * scale)));
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' '))
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
        public TextField? Field { get; init; }

        /// <summary>A slider bar: clicking or dragging along it sets this slider.</summary>
        public MapSlider? Bar { get; init; }

        public bool Primary { get; init; }

        public bool Selected { get; init; }

        public bool Contains(Vector2 point) => point.X >= X && point.X < X + W && point.Y >= Y && point.Y < Y + H;
    }
}
