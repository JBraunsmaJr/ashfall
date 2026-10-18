﻿using Ashfall.Content;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Players;
using static Ashfall.Simulation.Tests.TestMatch;

namespace Ashfall.Simulation.Tests;

public class CindriteTests
{
    private static EconomyRules Rules => GameContent.Standard.Economy;

    [Fact]
    public void Fields_stay_harvested_unless_the_match_has_regrowth()
    {
        var sim = Create();
        sim.Map.SetCindrite(new Cell(5, 5), 400);
        sim.Map.TakeCindrite(new Cell(5, 5), 100);

        Run(sim, Rules.CindriteRegrowIntervalTicks * 3);

        Assert.Equal(300, sim.Map.CindriteAt(new Cell(5, 5)));
        Assert.Equal(400, sim.Map.CindriteCapAt(new Cell(5, 5)));
    }

    [Fact]
    public void A_harvested_cell_grows_back_to_the_density_it_was_laid_with()
    {
        var sim = WithRegrowth();
        var cell = new Cell(5, 5);
        sim.Map.SetCindrite(cell, 400);
        sim.Map.TakeCindrite(cell, 12);
        var total = sim.Map.TotalCindrite;

        Run(sim, Rules.CindriteRegrowIntervalTicks + 1); // ticks 0 .. interval: one regrowth step
        Assert.Equal(388 + Rules.CindriteRegrowAmount, sim.Map.CindriteAt(cell));
        Assert.Equal(total + Rules.CindriteRegrowAmount, sim.Map.TotalCindrite);

        Run(sim, Rules.CindriteRegrowIntervalTicks * 5);
        Assert.Equal(400, sim.Map.CindriteAt(cell)); // never past its cap
    }

    [Fact]
    public void An_emptied_cell_regrows_only_beside_living_field()
    {
        var sim = WithRegrowth();
        var map = sim.Map;
        for (var x = 4; x <= 6; x++)
        {
            map.SetCindrite(new Cell(x, 5), 400);
        }

        map.SetCindrite(new Cell(10, 10), 400);
        map.TakeCindrite(new Cell(5, 5), 400);   // emptied, but its neighbours are full
        map.TakeCindrite(new Cell(10, 10), 400); // emptied, nothing around it

        Run(sim, Rules.CindriteRegrowIntervalTicks + 1);

        Assert.Equal(Rules.CindriteRegrowAmount, map.CindriteAt(new Cell(5, 5)));
        Assert.Equal(0, map.CindriteAt(new Cell(10, 10)));
        Assert.Equal(0, map.CindriteAt(new Cell(5, 6))); // never was a field: no cap, no growth
    }

    [Fact]
    public void A_field_picked_clean_stays_dead()
    {
        var sim = WithRegrowth();
        for (var x = 4; x <= 6; x++)
        {
            for (var y = 4; y <= 6; y++)
            {
                sim.Map.SetCindrite(new Cell(x, y), 400);
                sim.Map.TakeCindrite(new Cell(x, y), 400);
            }
        }

        Run(sim, Rules.CindriteRegrowIntervalTicks * 4);

        Assert.Equal(0, sim.Map.TotalCindrite);
    }

    private static GameSimulation WithRegrowth() =>
        new(7, new GridMap(32, 32), TestMatch.Content,
            [new PlayerSetup(0, BulwarkContent.FactionId), new PlayerSetup(1, BulwarkContent.FactionId)],
            victoryEnabled: false, cindriteRegrowth: true);
}
