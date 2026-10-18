﻿using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Components;

/// <summary>
/// Marks a Maw nest (a structure whose definition has the Nest role): the biomass it has stored from feeding on
/// Cindrite, and the ticks until it may spawn again. Its rules come from <see cref="Content.GameContent.NestFor"/>.
/// </summary>
public record struct Nest(int Biomass, int SpawnTimer) : IStateHashable
{
    public readonly void AddTo(ref StateHasher hasher)
    {
        hasher.Add(Biomass);
        hasher.Add(SpawnTimer);
    }
}
