using System.Globalization;
using System.Numerics;
using Ashfall.Networking.Protocol;
using Ashfall.Rendering;
using Ashfall.Rendering.Scene;
using Ashfall.Session.Network;
using Ashfall.UI.Drawing;
using Ashfall.UI.Input;
using Ashfall.UI.Text;

namespace Ashfall.UI;

/// <summary>
/// The multiplayer lobby: every slot with its player, team and readiness, plus the controls this
/// player has: change team, toggle ready, and (host) change AI teams and start. Also shows the
/// connection status and recent events. Drawn with the same pixel font and panels as the sidebar.
/// </summary>
public sealed class LobbyScreen(NetworkSession session)
{
    private readonly List<(float X, float Y, float W, float H, Action Click, bool Enabled)> _buttons = [];
    private Vector2 _framebuffer = new(1280, 720);
    private Vector2 _mouse;

    /// <summary>Set when the player presses Esc to leave the lobby.</summary>
    public bool ExitRequested { get; private set; }

    public NetworkSession Session { get; } = session;

    public void Update(InputState input, Vector2 framebuffer)
    {
        ArgumentNullException.ThrowIfNull(input);
        _framebuffer = framebuffer;
        _mouse = input.Mouse;
        if (input.WasPressed(Key.Escape))
        {
            ExitRequested = true;
        }

        Layout();
        if (!input.WasPressed(MouseButton.Left))
        {
            return;
        }

        foreach (var (x, y, w, h, click, enabled) in _buttons)
        {
            if (enabled && input.Mouse.X >= x && input.Mouse.X < x + w && input.Mouse.Y >= y && input.Mouse.Y < y + h)
            {
                click();
                return;
            }
        }
    }

    public void Draw(FrameScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.Clear();
        scene.ClearColor = Palette.Clear;
        var canvas = new Canvas(scene);
        var scale = Scale;
        var panelWidth = Math.Min(_framebuffer.X - (40 * scale), 760 * scale);
        var left = (_framebuffer.X - panelWidth) / 2f;
        var top = 40 * scale;
        canvas.Rect(left, top, panelWidth, _framebuffer.Y - (80 * scale), Palette.Panel);
        canvas.Frame(left, top, panelWidth, _framebuffer.Y - (80 * scale), 2 * scale, Palette.PanelEdge);

        canvas.Text(left + (16 * scale), top + (16 * scale), "MULTIPLAYER LOBBY", 3 * scale, Palette.Credits);
        canvas.TextRight(left + panelWidth - (16 * scale), top + (22 * scale), StatusText(), 2 * scale, StatusColor());

        var lobby = Session.Lobby;
        if (lobby is not null)
        {
            var rowY = top + (64 * scale);
            canvas.Text(left + (16 * scale), rowY, "SLOT  PLAYER            TEAM   STATUS", 2 * scale, Palette.TextDim);
            rowY += 22 * scale;
            for (var slot = 0; slot < lobby.Slots.Count; slot++)
            {
                DrawSlot(canvas, lobby, slot, left + (16 * scale), rowY, panelWidth - (32 * scale));
                rowY += 30 * scale;
            }
        }

        foreach (var (x, y, w, h, _, enabled) in _buttons)
        {
            var hovered = enabled && _mouse.X >= x && _mouse.X < x + w && _mouse.Y >= y && _mouse.Y < y + h;
            canvas.Rect(x, y, w, h, !enabled ? Palette.Button : hovered ? Palette.ButtonHover : Palette.PanelLight);
            canvas.Frame(x, y, w, h, scale, enabled ? Palette.PanelEdge : Palette.Button);
        }

        foreach (var (x, y, w, h, label, enabled) in Labels())
        {
            canvas.TextCentered(x + (w / 2), y + ((h - (PixelFont.GlyphHeight * 2 * scale)) / 2), label, 2 * scale, enabled ? Palette.Text : Palette.TextDim);
        }

        // Recent events, newest last.
        var events = Session.Events;
        var logTop = _framebuffer.Y - (60 * scale) - (8 * 12 * scale);
        canvas.Text(left + (16 * scale), logTop - (16 * scale), "EVENTS", scale * 1.5f, Palette.TextDim);
        var first = Math.Max(0, events.Count - 8);
        for (var i = first; i < events.Count; i++)
        {
            canvas.Text(left + (16 * scale), logTop + ((i - first) * 12 * scale), Trim(events[i], panelWidth - (32 * scale), 1.5f * scale), 1.5f * scale, Palette.Text);
        }

        canvas.Text(left + (16 * scale), _framebuffer.Y - (56 * scale), "ESC LEAVES", 1.5f * scale, Palette.TextDim);
    }

    private float Scale => _framebuffer.Y >= 1300 ? 2f : _framebuffer.Y >= 960 ? 1.5f : 1f;

    private void DrawSlot(Canvas canvas, LobbyState lobby, int slot, float x, float y, float width)
    {
        var scale = Scale;
        var info = lobby.Slots[slot];
        var mine = slot == Session.LocalSlot;
        if (mine)
        {
            canvas.Rect(x - (4 * scale), y - (5 * scale), width + (8 * scale), 24 * scale, Palette.WithAlpha(Palette.ButtonActive, 0.6f));
        }

        var name = info.Kind switch
        {
            SlotKind.Human => info.Name.ToUpperInvariant(),
            SlotKind.Ai => "AI (SCRIPTED)",
            SlotKind.Closed => "CLOSED",
            _ => "OPEN",
        };
        var color = info.Kind is SlotKind.Open or SlotKind.Closed ? Palette.TextDim : Palette.Text;
        canvas.Text(x, y, (slot + 1).ToString(CultureInfo.InvariantCulture), 2 * scale, Palette.TextDim);
        canvas.Rect(x + (40 * scale), y, 12 * scale, 14 * scale, Palette.Player(lobby.SeatOfSlot(slot)));
        canvas.Text(x + (60 * scale), y, name, 2 * scale, color);
        canvas.Text(x + (276 * scale), y, (info.Team + 1).ToString(CultureInfo.InvariantCulture), 2 * scale, Palette.Text);

        var status = info.Kind switch
        {
            SlotKind.Human when slot == 0 => "HOST",
            SlotKind.Human => info.Ready ? "READY" : "NOT READY",
            SlotKind.Ai => "READY",
            _ => string.Empty,
        };
        if (info.Kind == SlotKind.Human && !mine && Session.RoundTripToSlot(slot) is { } rtt)
        {
            status += string.Create(CultureInfo.InvariantCulture, $" {rtt.TotalMilliseconds:F0}MS");
        }

        canvas.Text(x + (360 * scale), y, status, 2 * scale, info.Ready || slot == 0 ? Palette.TextGood : Palette.TextDim);
    }

    // Buttons: per-slot team buttons (own slot; AI slots for the host), then READY and START.
    private void Layout()
    {
        _buttons.Clear();
        var lobby = Session.Lobby;
        if (lobby is null || Session.State != SessionState.Lobby)
        {
            return;
        }

        var scale = Scale;
        var panelWidth = Math.Min(_framebuffer.X - (40 * scale), 760 * scale);
        var left = (_framebuffer.X - panelWidth) / 2f;
        var rowY = (40 * scale) + (64 * scale) + (22 * scale);
        for (var slot = 0; slot < lobby.Slots.Count; slot++)
        {
            var info = lobby.Slots[slot];
            var canChange = slot == Session.LocalSlot || (Session.IsHost && info.Kind == SlotKind.Ai);
            if (canChange)
            {
                var target = slot;
                var nextTeam = (info.Team + 1) % Math.Max(2, lobby.Slots.Count);
                _buttons.Add((left + 16 * scale + 240 * scale, rowY - (4 * scale), 28 * scale, 22 * scale,
                    () =>
                    {
                        if (target == Session.LocalSlot)
                        {
                            Session.SetTeam(nextTeam);
                        }
                        else
                        {
                            Session.SetSlotTeam(target, nextTeam);
                        }
                    },
                    true));
            }

            rowY += 30 * scale;
        }

        var buttonY = rowY + (16 * scale);
        var mine = Session.LocalSlot >= 0 && Session.LocalSlot < lobby.Slots.Count ? lobby.Slots[Session.LocalSlot] : null;
        if (!Session.IsHost && mine is not null)
        {
            _buttons.Add((left + (16 * scale), buttonY, 180 * scale, 30 * scale, () => Session.SetReady(!mine.Ready), Session.MeshComplete));
        }

        if (Session.IsHost)
        {
            _buttons.Add((left + (16 * scale), buttonY, 180 * scale, 30 * scale, Session.StartMatch, Session.StartBlocker is null));
        }
    }

    private IEnumerable<(float X, float Y, float W, float H, string Label, bool Enabled)> Labels()
    {
        var lobby = Session.Lobby;
        foreach (var (x, y, w, h, _, enabled) in _buttons)
        {
            string label;
            if (w < 40 * Scale)
            {
                label = "+";
            }
            else if (Session.IsHost)
            {
                label = "START";
            }
            else
            {
                var mine = lobby is not null && Session.LocalSlot >= 0 ? lobby.Slots[Session.LocalSlot] : null;
                label = mine?.Ready == true ? "NOT READY" : "READY";
            }

            yield return (x, y, w, h, label, enabled);
        }
    }

    private string StatusText() => Session.State switch
    {
        SessionState.Connecting => "CONNECTING...",
        SessionState.Failed => "FAILED",
        SessionState.Lobby when Session.IsHost => Session.StartBlocker is { } blocker ? blocker.ToUpperInvariant() : "READY TO START",
        SessionState.Lobby => Session.MeshComplete ? "CONNECTED TO ALL PLAYERS" : "CONNECTING TO PLAYERS...",
        _ => Session.State.ToString().ToUpperInvariant(),
    };

    private LinearColor StatusColor() => Session.State switch
    {
        SessionState.Failed => Palette.TextWarn,
        SessionState.Lobby when Session.IsHost && Session.StartBlocker is null => Palette.TextGood,
        SessionState.Lobby when !Session.IsHost && Session.MeshComplete => Palette.TextGood,
        _ => Palette.TextDim,
    };

    private static string Trim(string text, float width, float scale)
    {
        var upper = text.ToUpperInvariant();
        var max = (int)(width / (PixelFont.Advance * scale));
        return upper.Length <= max ? upper : upper[..Math.Max(0, max - 3)] + "...";
    }
}
