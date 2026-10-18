namespace Ashfall.Simulation;

/// <summary>How a match with victory enabled is won (see <see cref="Systems.VictorySystem"/>).</summary>
public enum MatchGoal
{
    /// <summary>Players are defeated when they have no structures left; the last team standing wins.</summary>
    Elimination,

    /// <summary>
    /// Co-op against the Maw (GDD section 2, "Purge"): the players win together once every nest is destroyed, and
    /// the Maw wins (<see cref="GameSimulation.MawTeam"/>) once every player has lost all their structures.
    /// </summary>
    Purge,
}
