namespace Ashfall.Session;

/// <summary>The state hash after a tick; peers exchange these every 30 ticks to detect desyncs.</summary>
public readonly record struct HashCheckpoint(long Tick, ulong StateHash);
