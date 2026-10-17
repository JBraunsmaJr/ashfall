namespace Ashfall.Simulation.Systems;

/// <summary>
/// A stateless step of the simulation. Systems run in a fixed order every tick; all state lives in
/// components so snapshots and hashes capture everything.
/// </summary>
public interface ISystem
{
    void Update(in TickContext context);
}
