﻿using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>
/// Marks a Maw nest (a structure whose definition has the Nest role): the biomass it has stored from feeding on
/// Cindrite, the ticks until it may spawn again, how much it has eaten at its current tier (it evolves at the
/// threshold), and the creature it is saving up for (-1 until it picks one from its spawn table). Its rules come from
/// <see cref="Content.GameContent.NestFor"/>.
/// </summary>
public record struct Nest(int Biomass, int SpawnTimer, int Eaten = 0, int NextCreature = -1) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Biomass);
        hasher.Add(SpawnTimer);
        hasher.Add(Eaten);
        hasher.Add(NextCreature);
    }
}
