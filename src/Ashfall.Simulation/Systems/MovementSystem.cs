using Ashfall.Core;
using Ashfall.Simulation.Components;

namespace Ashfall.Simulation.Systems;

/// <summary>Moves every entity with a target straight toward it at its speed, stopping exactly on arrival.</summary>
public sealed class MovementSystem : ISystem
{
    public void Update(in TickContext context)
    {
        var movers = context.World.Store<Mover>();
        var positions = context.World.Store<Position>();
        var entityIndices = movers.EntityIndices;
        var moverSpan = movers.Components;

        for (var i = 0; i < moverSpan.Length; i++)
        {
            ref var mover = ref moverSpan[i];
            if (!mover.HasTarget)
            {
                continue;
            }

            ref var position = ref positions.Get(entityIndices[i]);
            var toTarget = mover.Target - position.Value;
            var step = mover.SpeedPerTick;

            if (toTarget.LengthSquared <= step * step)
            {
                position.Value = mover.Target;
                mover.HasTarget = false;
                continue;
            }

            position.Value += toTarget.Normalized() * step;
        }
    }
}
