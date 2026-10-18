namespace Ashfall.Simulation.Players;

/// <summary>How a player enters the match. Player ids are positions in the setup list.</summary>
public readonly record struct PlayerSetup(int Team, string FactionId);
