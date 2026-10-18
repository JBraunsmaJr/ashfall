using System.Globalization;

namespace Ashfall.UI.Menu;

/// <summary>Parses what a player types into the join box: <c>host</c> or <c>host:port</c>.</summary>
public static class HostAddress
{
    public const int DefaultPort = 47600;

    public static bool TryParse(string? text, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var colon = trimmed.LastIndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(trimmed.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
            {
                return false;
            }

            trimmed = trimmed[..colon];
        }

        if (trimmed.Length == 0 || trimmed.Contains(':', StringComparison.Ordinal) || trimmed.StartsWith('-') || trimmed.StartsWith('.'))
        {
            return false;
        }

        host = trimmed;
        return true;
    }

    public static string Format(string host, int port) =>
        port == DefaultPort ? host : string.Create(CultureInfo.InvariantCulture, $"{host}:{port}");
}
