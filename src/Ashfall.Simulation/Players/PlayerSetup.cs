namespace Ashfall.Simulation.Players;

/// <summary>How a player enters the match. Player ids are positions in the setup list.</summary>
/// <param name="BonusCredits">Credits on top of the economy's starting credits (campaign bonuses from liberated neighbours).</param>
/// <param name="Modifiers">Lasting bonuses (campaign research, commander perks); null for none.</param>
public readonly record struct PlayerSetup(int Team, string FactionId, int BonusCredits = 0, PlayerModifiers? Modifiers = null);
