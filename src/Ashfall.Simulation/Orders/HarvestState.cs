namespace Ashfall.Simulation.Orders;

/// <summary>Steps of the harvesting loop. Values are part of the replay format: append only.</summary>
public enum HarvestState : byte
{
    Idle = 0,
    MovingToField = 1,
    Harvesting = 2,
    MovingToRefinery = 3,
    Unloading = 4,
}
