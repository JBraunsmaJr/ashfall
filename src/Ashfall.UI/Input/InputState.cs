using System.Numerics;

namespace Ashfall.UI.Input;

/// <summary>
/// One frame of input: where the mouse is, what went down or up since the last frame, which keys are
/// held. Built by the client from window events (or by tests directly) and handed to the match screen.
/// </summary>
public sealed class InputState
{
    private readonly HashSet<Key> _held = [];
    private readonly HashSet<Key> _pressed = [];
    private readonly System.Text.StringBuilder _typed = new();

    /// <summary>Mouse position in framebuffer pixels, origin top-left.</summary>
    public Vector2 Mouse { get; set; }

    public MouseButton ButtonsDown { get; set; }

    public MouseButton ButtonsPressed { get; set; }

    public MouseButton ButtonsReleased { get; set; }

    /// <summary>Wheel movement this frame: positive is away from the user.</summary>
    public float Scroll { get; set; }

    /// <summary>Whether the cursor is over the window (edge scrolling only applies then).</summary>
    public bool MouseInWindow { get; set; } = true;

    /// <summary>Seconds elapsed since the previous frame.</summary>
    public float DeltaSeconds { get; set; }

    /// <summary>Seconds since the screen started; used for double-taps.</summary>
    public double Time { get; set; }

    /// <summary>Text typed this frame (characters and pastes), for text fields.</summary>
    public string TypedText => _typed.ToString();

    public bool Shift => _held.Contains(Key.Shift);

    public bool Control => _held.Contains(Key.Control);

    public bool IsHeld(Key key) => _held.Contains(key);

    public bool WasPressed(Key key) => _pressed.Contains(key);

    public bool IsDown(MouseButton button) => (ButtonsDown & button) != 0;

    public bool WasPressed(MouseButton button) => (ButtonsPressed & button) != 0;

    public bool WasReleased(MouseButton button) => (ButtonsReleased & button) != 0;

    public void SetKey(Key key, bool down)
    {
        if (down)
        {
            if (_held.Add(key))
            {
                _pressed.Add(key);
            }
        }
        else
        {
            _held.Remove(key);
        }
    }

    /// <summary>Counts a held key as pressed again (keyboard auto-repeat, e.g. holding Backspace).</summary>
    public void RepeatKey(Key key)
    {
        if (_held.Contains(key))
        {
            _pressed.Add(key);
        }
    }

    /// <summary>Adds typed characters for this frame.</summary>
    public void Type(string text) => _typed.Append(text);

    public void SetButton(MouseButton button, bool down)
    {
        if (down && (ButtonsDown & button) == 0)
        {
            ButtonsDown |= button;
            ButtonsPressed |= button;
        }
        else if (!down && (ButtonsDown & button) != 0)
        {
            ButtonsDown &= ~button;
            ButtonsReleased |= button;
        }
    }

    /// <summary>Clears this frame's edges (presses, releases, scroll), keeping what is held.</summary>
    public void EndFrame()
    {
        _pressed.Clear();
        _typed.Clear();
        ButtonsPressed = MouseButton.None;
        ButtonsReleased = MouseButton.None;
        Scroll = 0;
    }
}
