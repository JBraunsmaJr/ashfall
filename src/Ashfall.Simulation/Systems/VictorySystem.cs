using Ashfall.Simulation.Components;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Default victory condition (GDD section 2): a player with no structures left is defeated; the match is
/// over when at most one team still has an undefeated player.
/// </summary>
public sealed class VictorySystem : ISystem
{
    private readonly List<int> _survivingTeams = [];
    private int[] _structureCounts = [];

    public void Update(in TickContext context)
    {
        var simulation = context.Simulation;
        var players = context.Players;
        if (!simulation.VictoryEnabled || simulation.IsOver || players.Count == 0)
        {
            return;
        }

        if (_structureCounts.Length != players.Count)
        {
            _structureCounts = new int[players.Count];
        }

        Array.Clear(_structureCounts);
        var structures = context.World.Store<StructureInfo>();
        var owners = context.World.Store<Owner>();
        foreach (var index in structures.EntityIndices)
        {
            var owner = owners.Get(index).PlayerId;
            if (owner >= 0 && owner < _structureCounts.Length && !context.World.Store<Health>().Get(index).IsDead)
            {
                _structureCounts[owner]++;
            }
        }

        _survivingTeams.Clear();
        foreach (var player in players)
        {
            if (!player.Defeated && _structureCounts[player.Id] == 0)
            {
                player.Defeated = true;
            }

            if (!player.Defeated && !_survivingTeams.Contains(player.Team))
            {
                _survivingTeams.Add(player.Team);
            }
        }

        if (_survivingTeams.Count <= 1)
        {
            simulation.IsOver = true;
            simulation.WinningTeam = _survivingTeams.Count == 1 ? _survivingTeams[0] : -1;
        }
    }
}
