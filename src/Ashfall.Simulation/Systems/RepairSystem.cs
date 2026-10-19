using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Players;

namespace Ashfall.Simulation.Systems;

/// <summary>
/// Mends what is hurt, every <see cref="EconomyRules.RepairIntervalTicks"/>: structures with repair switched on,
/// vehicles parked beside a repair bay (both paid for as they mend, <see cref="EconomyRules.RepairCostPercent"/> of what
/// the restored hit points cost new), and infantry near a medic (free).
/// </summary>
/// <remarks>
/// Each pulse restores <see cref="EconomyRules.RepairPercent"/> of the maximum (at least 1). A player who cannot pay
/// for a pulse just waits for the next; a structure that is whole again stops repairing. Everything is processed in
/// store order, so who is paid for first when credits run short is the same on every peer.
/// </remarks>
public sealed class RepairSystem : ISystem
{
    /// <summary>How close to a repair bay's footprint a vehicle must be, in cells.</summary>
    public static readonly Fix64 BayReach = Fix64.FromInt(2);

    private readonly List<int> _mended = [];

    public void Update(in TickContext context)
    {
        var rules = context.Content.Economy;
        if (context.Tick % rules.RepairIntervalTicks != 0)
        {
            return;
        }

        RepairStructures(context, rules);
        RepairAtBays(context, rules);
        Heal(context);
    }

    /// <summary>Credits a pulse restoring <paramref name="amount"/> of <paramref name="max"/> hit points costs on something priced <paramref name="price"/>.</summary>
    public static int CostOf(int price, int amount, int max, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var scaled = (long)price * amount * rules.RepairCostPercent;
        var divisor = 100L * max;
        return (int)((scaled + divisor - 1) / divisor);
    }

    /// <summary>Hit points one pulse restores to something with <paramref name="max"/> of them.</summary>
    public static int PulseOf(int max, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var amount = max * rules.RepairPercent / 100;
        return amount > 1 ? amount : 1;
    }

    private static void RepairStructures(in TickContext context, EconomyRules rules)
    {
        var world = context.World;
        var structures = world.Store<StructureInfo>();
        var indices = structures.EntityIndices;
        var infos = structures.Components;
        for (var i = 0; i < infos.Length; i++)
        {
            ref var info = ref infos[i];
            if (!info.Repairing)
            {
                continue;
            }

            var index = indices[i];
            var owner = world.Store<Owner>().Get(index).PlayerId;
            ref var health = ref world.Store<Health>().Get(index);
            if (health.Current >= health.Max || owner < 0 || owner >= context.Players.Count)
            {
                info.Repairing = false;
                continue;
            }

            Mend(context.Players[owner], ref health, context.Content.Structure(info.StructureType).Cost, rules);
            if (health.Current >= health.Max)
            {
                info.Repairing = false;
            }
        }
    }

    private void RepairAtBays(in TickContext context, EconomyRules rules)
    {
        var world = context.World;
        var structures = world.Store<StructureInfo>();
        var owners = world.Store<Owner>();
        var units = world.Store<UnitInfo>();
        _mended.Clear();
        foreach (var bay in structures.EntityIndices)
        {
            var info = structures.Get(bay);
            if (!info.IsComplete || context.Content.Structure(info.StructureType).Role != StructureRole.RepairBay)
            {
                continue;
            }

            var owner = owners.Get(bay).PlayerId;
            var bayEntity = world.EntityAt(bay);
            foreach (var index in units.EntityIndices)
            {
                var definition = context.Content.Unit(units.Get(index).UnitType);
                ref var health = ref world.Store<Health>().Get(index);
                if (owners.Get(index).PlayerId != owner || !definition.IsVehicle || health.Current >= health.Max || _mended.Contains(index))
                {
                    continue;
                }

                var position = world.Store<Position>().Get(index).Value;
                if (TargetingSystem.InRange(position, context.Simulation.ClosestPointOf(bayEntity, position), BayReach))
                {
                    Mend(context.Players[owner], ref health, definition.Cost, rules);
                    _mended.Add(index);
                }
            }
        }
    }

    // Every medic tends the worst hurt friendly infantry within its reach (lowest share of health left, then the
    // lowest index); never itself.
    private static void Heal(in TickContext context)
    {
        var world = context.World;
        var units = world.Store<UnitInfo>();
        var owners = world.Store<Owner>();
        var healths = world.Store<Health>();
        var positions = world.Store<Position>();
        foreach (var medic in units.EntityIndices)
        {
            var definition = context.Content.Unit(units.Get(medic).UnitType);
            if (!definition.IsMedic || healths.Get(medic).IsDead)
            {
                continue;
            }

            var owner = owners.Get(medic).PlayerId;
            var from = positions.Get(medic).Value;
            var best = -1;
            foreach (var index in units.EntityIndices)
            {
                var health = healths.Get(index);
                if (index == medic || health.IsDead || health.Current >= health.Max
                    || !context.Content.Unit(units.Get(index).UnitType).IsInfantry
                    || context.Simulation.AreEnemies(owner, owners.Get(index).PlayerId)
                    || !TargetingSystem.InRange(from, positions.Get(index).Value, definition.AcquisitionRange))
                {
                    continue;
                }

                if (best < 0 || (long)health.Current * healths.Get(best).Max < (long)healths.Get(best).Current * health.Max)
                {
                    best = index;
                }
            }

            if (best >= 0)
            {
                ref var patient = ref healths.Get(best);
                var room = patient.Max - patient.Current;
                patient.Current += definition.HealPerPulse < room ? definition.HealPerPulse : room;
            }
        }
    }

    private static void Mend(PlayerState player, ref Health health, int price, EconomyRules rules)
    {
        var room = health.Max - health.Current;
        var pulse = PulseOf(health.Max, rules);
        var amount = pulse < room ? pulse : room;
        var cost = CostOf(price, amount, health.Max, rules) * player.Modifiers.RepairCostPercent / 100;
        if (!player.CanAfford(cost))
        {
            return;
        }

        player.Spend(cost);
        health.Current += amount;
    }
}
