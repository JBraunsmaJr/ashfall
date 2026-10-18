namespace Ashfall.UI.Menu;

/// <summary>A finished menu decision, handed to the client to start a skirmish, host, join or quit.</summary>
public sealed record MenuChoice(MenuAction Action, MenuSettings Settings)
{
    /// <summary>For <see cref="MenuAction.Join"/>: the host name or address to connect to.</summary>
    public string JoinHost { get; init; } = string.Empty;

    /// <summary>For <see cref="MenuAction.Join"/>: the port to connect to.</summary>
    public int JoinPort { get; init; }
}
