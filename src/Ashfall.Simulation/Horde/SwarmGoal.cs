namespace Ashfall.Simulation.Horde;

/// <summary>
/// Where a marching creature is headed, packed in one int on <see cref="Components.Creature.Goal"/>: 0 for the
/// nearest player structure (the shared swarm field), otherwise one player's base or, for a raid, that player's
/// refineries. Each goal has its own flow field (<see cref="GameSimulation.FieldFor"/>).
/// </summary>
public static class SwarmGoal
{
    /// <summary>The nearest player structure, whoever owns it.</summary>
    public const int Nearest = 0;

    public static int Base(int player) => 1 + (player * 2);

    public static int Refineries(int player) => 2 + (player * 2);

    /// <summary>The player a goal is aimed at, or -1 for <see cref="Nearest"/>.</summary>
    public static int PlayerOf(int goal) => goal <= 0 ? -1 : (goal - 1) / 2;

    public static bool IsRaid(int goal) => goal > 0 && goal % 2 == 0;
}
