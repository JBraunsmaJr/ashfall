using Ashfall.Networking.Protocol;

namespace Ashfall.Session.Network;

/// <summary>
/// Who this player is on the network: an anonymous id generated on first launch and a free-text
/// display name. Nothing here identifies the person or their machine.
/// </summary>
public sealed record NetworkIdentity(PeerId Peer, string Name)
{
    public static NetworkIdentity CreateRandom(string name) => new(PeerId.NewRandom(), Clean(name));

    /// <summary>Reads the id stored at <paramref name="path"/>, creating it on first use, so rejoins are recognised.</summary>
    public static NetworkIdentity LoadOrCreate(string path, string name)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (File.Exists(path))
        {
            try
            {
                return new NetworkIdentity(PeerId.Parse(File.ReadAllText(path)), Clean(name));
            }
            catch (FormatException)
            {
                // Corrupt or hand-edited: start over with a fresh id.
            }
        }

        var identity = CreateRandom(name);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, identity.Peer.ToHex());
        return identity;
    }

    /// <summary>The default identity file: per user, outside the game folder.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Ashfall", "identity");

    // Names are shown in the pixel font: keep them short and printable.
    private static string Clean(string name)
    {
        var printable = new string((name ?? string.Empty).Where(c => c >= ' ' && c < 127).ToArray()).Trim();
        return printable.Length == 0 ? "PLAYER" : printable.Length > 16 ? printable[..16] : printable;
    }
}
