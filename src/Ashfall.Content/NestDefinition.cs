﻿namespace Ashfall.Content;

/// <summary>
/// How a Maw nest lives (GDD section 3): it draws Cindrite from the fields around it into biomass and spends
/// biomass on creatures. A nest whose fields are harvested out or purged starves and stops spawning.
/// </summary>
/// <param name="StructureId">The nest's structure (footprint, health, Carapace armour).</param>
/// <param name="CreatureId">The creature it spawns.</param>
/// <param name="FeedRadius">How far from the nest's centre it draws Cindrite, in cells.</param>
/// <param name="FeedIntervalTicks">Ticks between feeds.</param>
/// <param name="FeedAmount">Cindrite taken per feed (it becomes the same amount of biomass).</param>
/// <param name="MaxBiomass">The most biomass a nest can store.</param>
/// <param name="StartingBiomass">Biomass a nest has when it appears.</param>
/// <param name="SpawnIntervalTicks">Ticks between spawns while it can pay for a creature.</param>
public sealed record NestDefinition(
    string StructureId,
    string CreatureId,
    int FeedRadius,
    int FeedIntervalTicks,
    int FeedAmount,
    int MaxBiomass,
    int StartingBiomass,
    int SpawnIntervalTicks);
