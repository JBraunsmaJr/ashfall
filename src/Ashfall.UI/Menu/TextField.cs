using Ashfall.UI.Input;

namespace Ashfall.UI.Menu;

/// <summary>
/// A one-line text box for the menus: takes typed (and pasted) characters while focused, Backspace
/// deletes. Characters the field does not accept are dropped, so a field can only ever hold valid input.
/// </summary>
public sealed class TextField(string label, int maxLength, Func<char, bool> accepts)
{
    private readonly Func<char, bool> _accepts = accepts ?? throw new ArgumentNullException(nameof(accepts));
    private string _text = string.Empty;

    public string Label { get; } = label;

    public int MaxLength { get; } = maxLength;

    public bool Focused { get; set; }

    public string Text
    {
        get => _text;
        set => _text = Filter(value ?? string.Empty);
    }

    /// <summary>Applies this frame's typing when focused. Returns whether the text changed.</summary>
    public bool Edit(InputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Focused)
        {
            return false;
        }

        var before = _text;
        if (input.WasPressed(Key.Backspace) && _text.Length > 0)
        {
            _text = _text[..^1];
        }

        _text = Filter(_text + input.TypedText);
        return _text != before;
    }

    private string Filter(string text)
    {
        var kept = new System.Text.StringBuilder(Math.Min(text.Length, MaxLength));
        foreach (var c in text)
        {
            if (kept.Length < MaxLength && _accepts(c))
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }

    public static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-' or '.';

    public static bool IsAddressChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or ':' or '-';
}
