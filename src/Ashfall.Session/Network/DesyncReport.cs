namespace Ashfall.Session.Network;

/// <summary>Peers disagreed on the state hash after <paramref name="Tick"/>.</summary>
/// <param name="Hashes">Each human seat's reported hash for that tick.</param>
/// <param name="DumpPath">The state dump written for this peer, if dumps are enabled.</param>
public sealed record DesyncReport(long Tick, IReadOnlyDictionary<int, ulong> Hashes, string? DumpPath);
