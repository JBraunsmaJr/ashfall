﻿namespace Ashfall.Content;

/// <summary>
/// How a Maw nest lives (GDD section 3): it draws Cindrite from the fields around it into biomass, spends biomass on
/// creatures drawn from its spawn table, and once it has eaten enough evolves into the next tier. A nest whose fields
/// are harvested out or purged starves: it stops spawning and stops growing.
/// </summary>
/// <param name="StructureId">The nest's structure (footprint, health, Carapace armour).</param>
/// <param name="Spawns">What it breeds, and how often each relative to the others.</param>
/// <param name="FeedRadius">How far from the nest's centre it draws Cindrite, in cells.</param>
/// <param name="FeedIntervalTicks">Ticks between feeds.</param>
/// <param name="FeedAmount">Cindrite taken per feed (it becomes the same amount of biomass).</param>
/// <param name="MaxBiomass">The most biomass a nest can store.</param>
/// <param name="StartingBiomass">Biomass a nest has when it appears.</param>
/// <param name="SpawnIntervalTicks">Ticks between spawns while it can pay for the next creature.</param>
/// <param name="EvolvesTo">The nest structure this one becomes, or null for the last tier. Same footprint.</param>
/// <param name="EvolveAfter">Biomass it must eat (not store) at this tier before it evolves.</param>
public sealed record NestDefinition(
    string StructureId,
    IReadOnlyList<SpawnWeight> Spawns,
    int FeedRadius,
    int FeedIntervalTicks,
    int FeedAmount,
    int MaxBiomass,
    int StartingBiomass,
    int SpawnIntervalTicks,
    string? EvolvesTo = null,
    int EvolveAfter = 0);

/// <summary>One line of a nest's spawn table: a creature and its relative weight.</summary>
public sealed record SpawnWeight(string CreatureId, int Weight);
