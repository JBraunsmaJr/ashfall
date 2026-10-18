using System.Numerics;
using Ashfall.UI.Input;
using Brutal.GlfwApi;

namespace Ashfall.Client;

/// <summary>Feeds GLFW window events into the UI's <see cref="InputState"/>.</summary>
internal sealed class GlfwInput
{
    private readonly GlfwWindow _window;

    public GlfwInput(GlfwWindow window, InputState state)
    {
        _window = window;
        State = state;
        window.OnKey += (_, key, _, action, _) =>
        {
            var mapped = Map(key);
            if (mapped == Key.None)
            {
                // Ctrl+V pastes into whichever text field has focus.
                if (key == GlfwKey.V && action == GlfwKeyAction.Press && State.Control && Glfw.Clipboard is { } clipboard)
                {
                    State.Type(clipboard);
                }
            }
            else if (action == GlfwKeyAction.Repeat)
            {
                if (mapped == Key.Backspace)
                {
                    State.RepeatKey(mapped);
                }
            }
            else
            {
                State.SetKey(mapped, action == GlfwKeyAction.Press);
            }
        };
        window.OnChar += (_, codepoint) =>
        {
            // The pixel font is ASCII; anything else would print as a box.
            if (codepoint is >= 32 and < 127)
            {
                State.Type(((char)codepoint).ToString());
            }
        };
        window.OnMouseButton += (_, button, action, _) =>
        {
            var mapped = button switch
            {
                GlfwMouseButton.Left => MouseButton.Left,
                GlfwMouseButton.Right => MouseButton.Right,
                GlfwMouseButton.Middle => MouseButton.Middle,
                _ => MouseButton.None,
            };
            if (mapped != MouseButton.None)
            {
                State.SetButton(mapped, action == GlfwButtonAction.Press);
            }
        };
        window.OnScroll += (_, offset) => State.Scroll += (float)offset.Y;
        window.OnCursorEnter += (_, entered) => State.MouseInWindow = entered;
    }

    public InputState State { get; }

    /// <summary>Copies the cursor position (window coordinates scaled to framebuffer pixels).</summary>
    public void Poll()
    {
        var cursor = _window.CursorPos;
        var size = _window.Size;
        var framebuffer = _window.FramebufferSize;
        var scaleX = size.X > 0 ? framebuffer.X / (float)size.X : 1f;
        var scaleY = size.Y > 0 ? framebuffer.Y / (float)size.Y : 1f;
        State.Mouse = new Vector2((float)cursor.X * scaleX, (float)cursor.Y * scaleY);
    }

    private static Key Map(GlfwKey key) => key switch
    {
        >= GlfwKey.Number0 and <= GlfwKey.Number9 => Key.D0 + (key - GlfwKey.Number0),
        >= GlfwKey.Kp0 and <= GlfwKey.Kp9 => Key.D0 + (key - GlfwKey.Kp0),
        GlfwKey.A => Key.A,
        GlfwKey.G => Key.G,
        GlfwKey.H => Key.H,
        GlfwKey.P => Key.P,
        GlfwKey.S => Key.S,
        GlfwKey.Escape => Key.Escape,
        GlfwKey.Left => Key.Left,
        GlfwKey.Right => Key.Right,
        GlfwKey.Up => Key.Up,
        GlfwKey.Down => Key.Down,
        GlfwKey.Equal or GlfwKey.KpAdd => Key.Plus,
        GlfwKey.Minus or GlfwKey.KpSubtract => Key.Minus,
        GlfwKey.LeftShift or GlfwKey.RightShift => Key.Shift,
        GlfwKey.LeftControl or GlfwKey.RightControl => Key.Control,
        GlfwKey.Backspace => Key.Backspace,
        GlfwKey.Enter or GlfwKey.KpEnter => Key.Enter,
        GlfwKey.Tab => Key.Tab,
        _ => Key.None,
    };
}
