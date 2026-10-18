using System.Globalization;
using System.Numerics;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.UI.Drawing;
using Ashfall.UI.Input;
using Ashfall.UI.Text;

namespace Ashfall.UI.Menu;

/// <summary>
/// The title screen: start a skirmish, host a lobby or join one by address, without any command-line
/// flags. Each page is laid out fresh every frame from the same widget list that handles clicks, so
/// what is drawn is exactly what can be clicked. Tab moves between text boxes, Enter confirms, Esc goes back.
/// </summary>
public sealed class MainMenuScreen
{
    private const int MaxPlayers = 4;

    private readonly List<Widget> _widgets = [];
    private readonly List<(float X, float Y, string Text, float Size, LinearColor Color, bool Centered)> _labels = [];
    private readonly TextField _name = new("NAME", 16, TextField.IsNameChar);
    private readonly TextField _seed = new("SEED", 19, char.IsAsciiDigit);
    private readonly TextField _port = new("PORT", 5, char.IsAsciiDigit);
    private readonly TextField _address = new("ADDRESS", 64, TextField.IsAddressChar);
    private readonly IReadOnlyList<string> _recentHosts;
    private Page _page = Page.Main;
    private bool _idleOpponent;
    private int _humans;
    private int _ais;
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
        Message = message;
    }

    private enum Page
    {
        Main,
        Skirmish,
        Host,
        Join,
    }

    /// <summary>Set once the player confirms a page (or quits); the client acts on it.</summary>
    public MenuChoice? Choice { get; private set; }

    /// <summary>A line shown under the main buttons, e.g. why the last connection failed.</summary>
    public string? Message { get; set; }

    /// <summary>The page on screen: Main, Skirmish, Host or Join.</summary>
    public string CurrentPage => _page.ToString();

    /// <summary>The settings as currently entered (what gets remembered for next time).</summary>
    public MenuSettings Settings => new()
    {
        Name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "PLAYER",
        Seed = ulong.TryParse(_seed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ? seed : Ashfall.Session.ProvingGround.DefaultSeed,
        IdleOpponent = _idleOpponent,
        Humans = _humans,
        Ais = _ais,
        Port = ParsePort(_port.Text) ?? HostAddress.DefaultPort,
        RecentHosts = _recentHosts,
    };

    /// <summary>Every clickable thing on the current page with its label and centre, for tests and automation.</summary>
    public IEnumerable<(string Label, Vector2 Center, bool Enabled)> Targets
    {
        get
        {
            Layout();
            return _widgets.Select(w => (w.Field?.Label ?? w.Text, new Vector2(w.X + (w.W / 2), w.Y + (w.H / 2)), w.Enabled)).ToList();
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
            field.Edit(input);
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

            var fill = !widget.Enabled ? Palette.Button : widget.Selected ? Palette.ButtonActive : hovered ? Palette.ButtonHover : Palette.PanelLight;
            canvas.Rect(widget.X, widget.Y, widget.W, widget.H, fill);
            canvas.Frame(widget.X, widget.Y, widget.W, widget.H, scale, widget.Enabled ? (widget.Primary ? Palette.Credits : Palette.PanelEdge) : Palette.Button);
            canvas.TextCentered(widget.X + (widget.W / 2), widget.Y + ((widget.H - (PixelFont.GlyphHeight * 2 * scale)) / 2), widget.Text, 2 * scale,
                widget.Enabled ? Palette.Text : Palette.TextDim);
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

    private (float Left, float Top, float Width, float Height) PanelRect()
    {
        var scale = Scale;
        var width = Math.Min(_framebuffer.X - (40 * scale), 560 * scale);
        var top = Math.Max(110 * scale, _framebuffer.Y * 0.2f);
        return ((_framebuffer.X - width) / 2f, top, width, Math.Max(100 * scale, _contentBottom - top + (8 * scale)));
    }

    // A few drifting embers behind the panel, so the title screen isn't a flat void.
    private void DrawBackdrop(Canvas canvas)
    {
        var scale = Scale;
        canvas.TextCentered(_framebuffer.X / 2, Math.Max(8 * scale, PanelRect().Top - (70 * scale)), "ASHFALL", 9 * scale, Palette.Cindrite);
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
        var scale = Scale;
        var (left, top, width, _) = PanelRect();
        var center = left + (width / 2);
        var y = top + (20 * scale);
        var inner = left + (24 * scale);
        var innerWidth = width - (48 * scale);
        var rowHeight = 30 * scale;
        var buttonHeight = 34 * scale;

        void Heading(string text)
        {
            _labels.Add((center, y, text, 3 * scale, Palette.Credits, true));
            y += 40 * scale;
        }

        void Note(string text, LinearColor color)
        {
            foreach (var line in Wrap(text, innerWidth, 1.5f * scale))
            {
                _labels.Add((inner, y, line, 1.5f * scale, color, false));
                y += 13 * scale;
            }

            y += 3 * scale;
        }

        void FieldRow(TextField field)
        {
            _labels.Add((inner, y + (8 * scale), field.Label, 2 * scale, Palette.TextDim, false));
            _widgets.Add(new Widget(inner + (130 * scale), y, innerWidth - (130 * scale), rowHeight, string.Empty, null, true) { Field = field });
            y += rowHeight + (10 * scale);
        }

        void Stepper(string label, int value, Action down, bool canDown, Action up, bool canUp)
        {
            _labels.Add((inner, y + (8 * scale), label, 2 * scale, Palette.TextDim, false));
            var x = inner + (130 * scale);
            _widgets.Add(new Widget(x, y, 30 * scale, rowHeight, "-", down, canDown));
            _labels.Add((x + (60 * scale), y + (8 * scale), value.ToString(CultureInfo.InvariantCulture), 2 * scale, Palette.Text, true));
            _widgets.Add(new Widget(x + (90 * scale), y, 30 * scale, rowHeight, "+", up, canUp));
            y += rowHeight + (10 * scale);
        }

        void Actions(string primary, Action confirm, bool enabled)
        {
            y += 10 * scale;
            var half = (innerWidth - (16 * scale)) / 2;
            _widgets.Add(new Widget(inner, y, half, buttonHeight, "BACK", () => Go(Page.Main), true));
            _widgets.Add(new Widget(inner + half + (16 * scale), y, half, buttonHeight, primary, confirm, enabled) { Primary = true });
            y += buttonHeight + (14 * scale);
        }

        switch (_page)
        {
            case Page.Main:
            {
                _labels.Add((center, y, "BULWARK DIRECTORATE - SKIRMISH AND PEER-TO-PEER MULTIPLAYER", 1.5f * scale, Palette.TextDim, true));
                y += 34 * scale;
                var buttonWidth = Math.Min(innerWidth, 300 * scale);
                foreach (var (label, page) in new[] { ("SKIRMISH", Page.Skirmish), ("HOST GAME", Page.Host), ("JOIN GAME", Page.Join) })
                {
                    var target = page;
                    _widgets.Add(new Widget(center - (buttonWidth / 2), y, buttonWidth, buttonHeight + (4 * scale), label, () => Go(target), true) { Primary = page == Page.Skirmish });
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

            case Page.Skirmish:
            {
                Heading("SKIRMISH");
                _labels.Add((inner, y + (8 * scale), "OPPONENT", 2 * scale, Palette.TextDim, false));
                var half = (innerWidth - (130 * scale) - (10 * scale)) / 2;
                _widgets.Add(new Widget(inner + (130 * scale), y, half, rowHeight, "SCRIPTED AI", () => _idleOpponent = false, true) { Selected = !_idleOpponent });
                _widgets.Add(new Widget(inner + (140 * scale) + half, y, half, rowHeight, "IDLE", () => _idleOpponent = true, true) { Selected = _idleOpponent });
                y += rowHeight + (10 * scale);
                _labels.Add((inner, y + (8 * scale), _seed.Label, 2 * scale, Palette.TextDim, false));
                _widgets.Add(new Widget(inner + (130 * scale), y, innerWidth - (250 * scale), rowHeight, string.Empty, null, true) { Field = _seed });
                _widgets.Add(new Widget(inner + innerWidth - (110 * scale), y, 110 * scale, rowHeight, "RANDOM", RandomSeed, true));
                y += rowHeight + (10 * scale);
                Note("ONE VERSUS ONE ON THE DUEL MAP. IDLE NEVER ACTS: USE IT TO LEARN THE CONTROLS.", Palette.TextDim);
                var seedOk = ulong.TryParse(_seed.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _);
                if (!seedOk)
                {
                    Note("ENTER A NUMBER FOR THE SEED.", Palette.TextWarn);
                }

                Actions("START", () => Choose(MenuAction.Skirmish), seedOk);
                break;
            }

            case Page.Host:
            {
                Heading("HOST GAME");
                FieldRow(_name);
                var total = _humans + _ais;
                Stepper("PLAYERS", _humans, () => _humans--, _humans > 1 && total > 2, () => _humans++, total < MaxPlayers);
                Stepper("AI", _ais, () => _ais--, _ais > 0 && total > 2, () => _ais++, total < MaxPlayers);
                FieldRow(_port);
                var port = ParsePort(_port.Text);
                Note(port is { } p
                    ? string.Create(CultureInfo.InvariantCulture, $"PLAYERS COUNTS YOU. FOR INTERNET PLAY, FORWARD UDP PORT {p} TO THIS PC.")
                    : "PORT MUST BE A NUMBER FROM 1 TO 65535.", port is null ? Palette.TextWarn : Palette.TextDim);
                Note("FRIENDS JOIN WITH YOUR PUBLIC IP ADDRESS (OR LAN ADDRESS AT HOME).", Palette.TextDim);
                var nameOk = _name.Text.Trim().Length > 0;
                if (!nameOk)
                {
                    Note("ENTER A NAME.", Palette.TextWarn);
                }

                Actions("HOST", () => Choose(MenuAction.Host), nameOk && port is not null);
                break;
            }

            case Page.Join:
            {
                Heading("JOIN GAME");
                FieldRow(_name);
                FieldRow(_address);
                var addressOk = HostAddress.TryParse(_address.Text, out _, out _);
                Note(addressOk || _address.Text.Length == 0
                    ? "THE HOST'S ADDRESS, E.G. 203.0.113.7 OR 192.168.1.20:47600 (PORT DEFAULTS TO 47600)."
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

    private void Go(Page page)
    {
        _page = page;
        Message = null;
        Focus(null);
        if (page == Page.Join)
        {
            Focus(_address);
        }
    }

    private void Focus(TextField? field)
    {
        foreach (var f in new[] { _name, _seed, _port, _address })
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

        public bool Primary { get; init; }

        public bool Selected { get; init; }

        public bool Contains(Vector2 point) => point.X >= X && point.X < X + W && point.Y >= Y && point.Y < Y + H;
    }
}
