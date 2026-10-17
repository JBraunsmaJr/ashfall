using Ashfall.Core;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Systems;

/// <summary>Everything a system may touch during one tick.</summary>
public readonly record struct TickContext(long Tick, World World, DeterministicRandom Random, MapBounds Bounds);
