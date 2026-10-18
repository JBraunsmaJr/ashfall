using Ashfall.Content;
using Ashfall.Core;
using Ashfall.Simulation.Commands;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;
using Ashfall.Simulation.Horde;
using Ashfall.Simulation.Map;
using Ashfall.Simulation.Navigation;
using Ashfall.Simulation.Orders;
using Ashfall.Simulation.Players;
using Ashfall.Simulation.Production;
using Ashfall.Simulation.Spatial;
using Ashfall.Simulation.Systems;
using Ashfall.Simulation.Vision;

namespace Ashfall.Simulation;

/// <summary>
/// The authoritative, deterministic game state. Given the same seed, map, content, players, setup calls
/// and the same command list for every tick, every instance reaches a bit-identical state on any machine.
/// </summary>
public sealed class GameSimulation
{
    /// <summary>
    /// The owner id of everything the Maw controls (GDD section 3). The Maw is not a player: it has no
    /// <see cref="PlayerState"/>, and it is the enemy of every player and of nothing else.
    /// </summary>
    public const int MawPlayerId = -2;

    private readonly ISystem[] _systems;
    private readonly PlayerState[] _players;
    private Command[] _commandScratch = new Command[64];

    /// <summary>An open map with no players or content rules in play: units only move (the M0 proving ground).</summary>
    public GameSimulation(ulong seed, MapBounds bounds)
        : this(seed, CreateOpenMap(bounds), GameContent.Standard, [], victoryEnabled: false)
    {
    }

    /// <param name="victoryEnabled">When true, a player with no structures is defeated and the match ends when one team remains.</param>
    /// <param name="horde">How the Maw spawns, or null for a match without a horde.</param>
    /// <param name="cindriteRegrowth">When true, harvested Cindrite fields grow back (see <see cref="CindriteRegrowthSystem"/>).</param>
    public GameSimulation(
        ulong seed,
        GridMap map,
        GameContent content,
        IReadOnlyList<PlayerSetup> players,
        bool victoryEnabled = true,
        HordeSettings? horde = null,
        bool cindriteRegrowth = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(players);

        Seed = seed;
        Map = map;
        Content = content;
        VictoryEnabled = victoryEnabled;
        CindriteRegrowth = cindriteRegrowth;
        Random = new DeterministicRandom(seed);
        World = new World();
        Navigator = new Navigator(map);
        Spatial = new SpatialGrid(map.Width, map.Height);
        SwarmField = new FlowField(map);
        if (horde is not null)
        {
            if (horde.CreatureType >= content.Creatures.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(horde), horde.CreatureType, "The horde's creature type is not in the content.");
            }

            Maw = new MawState(seed, horde);
        }
        Visibility = new VisibilityMap(map.Width, map.Height, players.Count);

        _players = new PlayerState[players.Count];
        for (var id = 0; id < players.Count; id++)
        {
            var faction = content.FactionIndex(players[id].FactionId);
            _players[id] = new PlayerState(id, players[id].Team, faction, content.Economy.StartingCredits);
        }

        // Registration order is part of the state hash: append new component types at the end.
        World.Register<Position>();
        World.Register<Mover>();
        World.Register<Owner>();
        World.Register<Health>();
        World.Register<UnitInfo>();
        World.Register<StructureInfo>();
        World.Register<Armament>();
        World.Register<UnitOrder>();
        World.Register<Harvester>();
        World.Register<Producer>();
        World.Register<Creature>();
        World.Register<Nest>();

        // System order is fixed and identical on every peer.
        _systems =
        [
            new PowerSystem(),
            new ConstructionSystem(),
            new ProductionSystem(),
            new CindriteRegrowthSystem(),
            new HarvestSystem(),
            new HordeSpawnSystem(),
            new NestSystem(),
            new TargetingSystem(),
            new MovementSystem(),
            new SwarmSystem(),
            new WeaponSystem(),
            new DeathSystem(),
            new VisionSystem(),
            new VictorySystem(),
        ];
    }

    public ulong Seed { get; }

    public GridMap Map { get; }

    public MapBounds Bounds => Map.Bounds;

    public GameContent Content { get; }

    public IReadOnlyList<PlayerState> Players => _players;

    public Navigator Navigator { get; }

    /// <summary>Units and creatures bucketed by cell, rebuilt each tick before targeting. Derived, never hashed.</summary>
    public SpatialGrid Spatial { get; }

    /// <summary>
    /// The swarm's flow field toward player structures. Derived from the map; call <see cref="RefreshSwarmField"/>
    /// before reading it so it reflects the current structure layout.
    /// </summary>
    public FlowField SwarmField { get; }

    /// <summary>The Maw's state in a match with a horde, or null.</summary>
    public MawState? Maw { get; }

    /// <summary>Every player's fog of war, updated every <see cref="VisionSystem.Period"/> ticks.</summary>
    public VisibilityMap Visibility { get; }

    public bool VictoryEnabled { get; }

    /// <summary>Whether Cindrite fields grow back in this match. Fixed when the match is created.</summary>
    public bool CindriteRegrowth { get; }

    /// <summary>Number of ticks simulated so far. The next call to <see cref="Step"/> runs this tick.</summary>
    public long Tick { get; private set; }

    public World World { get; }

    /// <summary>The match's single random stream. Only the simulation may draw from it.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>Commands rejected so far (invalid, unaffordable, not your unit). Part of the state hash.</summary>
    public long RejectedCommands { get; private set; }

    /// <summary>True once at most one team has undefeated players.</summary>
    public bool IsOver { get; internal set; }

    /// <summary>The team that won, or -1 while the match runs (or if everyone lost at once).</summary>
    public int WinningTeam { get; internal set; } = -1;

    /// <summary>Creates a bare mover with no content definition (the M0 proving ground's units).</summary>
    public Entity SpawnUnit(int playerId, FixVec2 position, Fix64 speedPerTick)
    {
        var unit = World.Create();
        World.Add(unit, new Position(Bounds.Clamp(position)));
        World.Add(unit, Mover.Idle(speedPerTick));
        World.Add(unit, new Owner(playerId));
        return unit;
    }

    /// <summary>Creates a unit of a content-defined type at a position.</summary>
    public Entity SpawnUnit(int playerId, int unitType, FixVec2 position)
    {
        var definition = Content.Unit(unitType);
        var unit = World.Create();
        World.Add(unit, new Position(Bounds.Clamp(position)));
        World.Add(unit, Mover.Idle(definition.SpeedPerTick));
        World.Add(unit, new Owner(playerId));
        World.Add(unit, new Health(definition.Health, definition.Health, definition.Armor));
        World.Add(unit, new UnitInfo(unitType));
        World.Add(unit, UnitOrder.Idle);
        if (definition.Weapon is not null)
        {
            World.Add(unit, new Armament(0, Entity.None));
        }

        if (definition.IsHarvester)
        {
            World.Add(unit, Harvester.Empty);
        }

        return unit;
    }

    /// <summary>Creates a Maw creature of a content-defined type at a position. It joins the swarm next tick.</summary>
    public Entity SpawnCreature(int creatureType, FixVec2 position)
    {
        var definition = Content.Creature(creatureType);
        var creature = World.Create();
        World.Add(creature, new Position(Bounds.Clamp(position)));
        World.Add(creature, new Owner(MawPlayerId));
        World.Add(creature, new Health(definition.Health, definition.Health, definition.Armor));
        World.Add(creature, new Creature(creatureType, 0, Entity.None));
        return creature;
    }

    /// <summary>
    /// Places a Maw nest (a structure with the Nest role) at <paramref name="origin"/>, stocked with its starting
    /// biomass. Nests only work in a match with a horde: the horde's population limit and random stream drive them.
    /// </summary>
    public Entity PlaceNest(int structureType, Cell origin)
    {
        var rules = Content.NestFor(structureType)
            ?? throw new ArgumentOutOfRangeException(nameof(structureType), structureType, "That structure is not a nest.");
        if (Maw is null)
        {
            throw new InvalidOperationException("Nests need a match with a horde.");
        }

        var nest = PlaceStructure(MawPlayerId, structureType, origin);
        World.Add(nest, new Nest(rules.StartingBiomass, rules.SpawnIntervalTicks));
        return nest;
    }

    /// <summary>Rebuilds <see cref="SwarmField"/> if the structure layout changed since it was built, and returns it.</summary>
    public FlowField RefreshSwarmField()
    {
        if (SwarmField.BuiltForVersion != Map.StructureVersion)
        {
            SwarmField.Rebuild(this);
        }

        return SwarmField;
    }

    /// <summary>
    /// Places a structure without cost or placement rules, for match setup (starting bases). Complete
    /// structures work immediately; incomplete ones start construction.
    /// </summary>
    public Entity PlaceStructure(int playerId, int structureType, Cell origin, bool complete = true)
    {
        var definition = Content.Structure(structureType);
        if (!IsFootprintFree(origin, definition.Width, definition.Height))
        {
            throw new InvalidOperationException($"Cannot place {definition.Name} at {origin}: the footprint is blocked.");
        }

        var structure = World.Create();
        var center = new FixVec2(
            Fix64.FromInt(origin.X) + Fix64.FromRatio(definition.Width, 2),
            Fix64.FromInt(origin.Y) + Fix64.FromRatio(definition.Height, 2));
        World.Add(structure, new Position(center));
        World.Add(structure, new Owner(playerId));
        World.Add(structure, new Health(definition.Health, definition.Health, definition.Armor));
        World.Add(structure, new StructureInfo(structureType, origin, complete ? 0 : definition.BuildTicks));
        if (definition.Role is StructureRole.Barracks or StructureRole.Factory)
        {
            World.Add(structure, new Producer());
        }

        Map.SetStructure(origin, definition.Width, definition.Height, structure.Index);
        return structure;
    }

    /// <summary>Why a structure cannot be placed, or <see cref="PlacementResult.Ok"/>.</summary>
    public PlacementResult CheckPlacement(int playerId, int structureType, Cell origin)
    {
        if (playerId < 0 || playerId >= _players.Length || _players[playerId].Defeated)
        {
            return PlacementResult.NotAPlayer;
        }

        if (structureType < 0 || structureType >= Content.Structures.Count)
        {
            return PlacementResult.UnknownStructure;
        }

        var player = _players[playerId];
        var definition = Content.Structure(structureType);
        var faction = Content.Faction(player.Faction);
        if (!faction.StructureIds.Contains(definition.Id) || definition.Role == StructureRole.ConstructionYard)
        {
            return PlacementResult.NotBuildable;
        }

        if (!HasCompletedRole(playerId, StructureRole.ConstructionYard))
        {
            return PlacementResult.NoConstructionYard;
        }

        // One structure at a time, as in the classic sidebar; walls are the exception, so a whole line can go up at once.
        if (World.IsAlive(player.ConstructionSite) && definition.Role != StructureRole.Wall)
        {
            return PlacementResult.AlreadyBuilding;
        }

        foreach (var role in definition.Prerequisites)
        {
            if (!HasCompletedRole(playerId, role))
            {
                return PlacementResult.MissingPrerequisite;
            }
        }

        if (!player.CanAfford(definition.Cost))
        {
            return PlacementResult.CannotAfford;
        }

        if (!IsFootprintFree(origin, definition.Width, definition.Height))
        {
            return PlacementResult.Blocked;
        }

        return IsNearOwnStructure(playerId, origin, definition.Width, definition.Height)
            ? PlacementResult.Ok
            : PlacementResult.TooFarFromBase;
    }

    /// <summary>True if the player owns a completed structure with this role.</summary>
    public bool HasCompletedRole(int playerId, StructureRole role)
    {
        var structures = World.Store<StructureInfo>();
        var owners = World.Store<Owner>();
        var indices = structures.EntityIndices;
        var infos = structures.Components;
        for (var i = 0; i < infos.Length; i++)
        {
            if (infos[i].IsComplete
                && owners.Get(indices[i]).PlayerId == playerId
                && Content.Structure(infos[i].StructureType).Role == role)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True if the two owners are players on different teams, or one is the Maw and the other a player.
    /// Units without a player are nobody's enemy.
    /// </summary>
    public bool AreEnemies(int playerA, int playerB)
    {
        if (playerA == MawPlayerId)
        {
            return IsPlayer(playerB);
        }

        if (playerB == MawPlayerId)
        {
            return IsPlayer(playerA);
        }

        return IsPlayer(playerA) && IsPlayer(playerB) && _players[playerA].Team != _players[playerB].Team;
    }

    /// <summary>True if <paramref name="owner"/> is one of this match's players (not the Maw, not neutral).</summary>
    public bool IsPlayer(int owner) => owner >= 0 && owner < _players.Length;

    /// <summary>The point of <paramref name="target"/> closest to <paramref name="from"/>: its position, or the nearest point of a structure's footprint.</summary>
    public FixVec2 ClosestPointOf(Entity target, FixVec2 from)
    {
        if (World.Has<StructureInfo>(target))
        {
            var info = World.Get<StructureInfo>(target);
            var definition = Content.Structure(info.StructureType);
            var minX = Fix64.FromInt(info.Origin.X);
            var minY = Fix64.FromInt(info.Origin.Y);
            return new FixVec2(
                Fix64.Clamp(from.X, minX, minX + Fix64.FromInt(definition.Width)),
                Fix64.Clamp(from.Y, minY, minY + Fix64.FromInt(definition.Height)));
        }

        return World.Get<Position>(target).Value;
    }

    /// <summary>Creates a unit at the nearest free cell outside a structure's front edge.</summary>
    public Entity SpawnUnitAt(Entity structure, int unitType)
    {
        var info = World.Get<StructureInfo>(structure);
        var owner = World.Get<Owner>(structure).PlayerId;
        var exit = ExitCell(info);
        var position = Navigator.TryFindNearestPassable(exit, maxRadius: 8, out var cell)
            ? cell.Center
            : World.Get<Position>(structure).Value;
        return SpawnUnit(owner, unitType, position);
    }

    /// <summary>The cell just below the middle of a structure's footprint, where units leave and harvesters dock.</summary>
    public Cell ExitCell(StructureInfo info)
    {
        var definition = Content.Structure(info.StructureType);
        return new Cell(info.Origin.X + (definition.Width / 2), info.Origin.Y + definition.Height);
    }

    /// <summary>Advances one tick: applies this tick's commands, runs every system, then applies destruction.</summary>
    /// <param name="commands">All commands scheduled for this tick, from every player. Order between players does not matter.</param>
    public void Step(ReadOnlySpan<Command> commands)
    {
        foreach (var command in SortByPlayer(commands))
        {
            if (!Apply(command))
            {
                RejectedCommands++;
            }
        }

        var context = new TickContext(Tick, this);
        foreach (var system in _systems)
        {
            system.Update(context);
        }

        World.FlushDestroyed();
        Tick++;
    }

    /// <summary>Hash of the complete simulation state, compared between peers to detect desyncs.</summary>
    public ulong ComputeStateHash()
    {
        var hasher = new StateHasher();
        hasher.Add(Tick);
        hasher.Add(Seed);
        var random = Random.Snapshot;
        hasher.Add(random.State);
        hasher.Add(random.Increment);
        hasher.Add(RejectedCommands);
        hasher.Add(IsOver);
        hasher.Add(WinningTeam);
        foreach (var player in _players)
        {
            player.HashInto(ref hasher);
        }

        Map.HashInto(ref hasher);
        World.HashInto(ref hasher);
        Navigator.Paths.HashInto(ref hasher);
        Visibility.HashInto(ref hasher);
        Maw?.HashInto(ref hasher); // after everything else, so matches without a horde hash exactly as before
        return hasher.Value;
    }

    private static GridMap CreateOpenMap(MapBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "Map must be at least one cell in each direction.");
        }

        return new GridMap(bounds.Width, bounds.Height);
    }

    private bool Apply(in Command command) => command.Kind switch
    {
        CommandKind.Move => ApplyMove(command),
        CommandKind.Stop => ApplyStop(command),
        CommandKind.Attack => ApplyAttack(command),
        CommandKind.AttackMove => ApplyAttackMove(command),
        CommandKind.Build => ApplyBuild(command),
        CommandKind.Train => ApplyTrain(command),
        CommandKind.None => false,
        _ => false,
    };

    private bool OwnsLiveUnit(in Command command) =>
        World.IsAlive(command.Unit)
        && World.Has<Owner>(command.Unit)
        && World.Get<Owner>(command.Unit).PlayerId == command.PlayerId
        && World.Has<Mover>(command.Unit);

    private bool ApplyMove(in Command command)
    {
        if (!OwnsLiveUnit(command))
        {
            return false;
        }

        SetOrder(command.Unit, new UnitOrder(OrderKind.Move, Entity.None, command.Target));
        if (World.Has<Harvester>(command.Unit))
        {
            World.Get<Harvester>(command.Unit).State = HarvestState.Idle;
        }

        ref var mover = ref World.Get<Mover>(command.Unit);
        Navigator.PlanRoute(command.Unit.Index, World.Get<Position>(command.Unit).Value, command.Target, ref mover);
        return true;
    }

    private bool ApplyStop(in Command command)
    {
        if (!OwnsLiveUnit(command))
        {
            return false;
        }

        ref var mover = ref World.Get<Mover>(command.Unit);
        mover.HasTarget = false;
        Navigator.Paths.Clear(command.Unit.Index);
        SetOrder(command.Unit, UnitOrder.Idle);
        if (World.Has<Armament>(command.Unit))
        {
            World.Get<Armament>(command.Unit).Target = Entity.None;
        }

        return true;
    }

    private bool ApplyAttack(in Command command)
    {
        if (!OwnsLiveUnit(command)
            || !World.Has<Armament>(command.Unit)
            || !World.IsAlive(command.TargetEntity)
            || !World.Has<Health>(command.TargetEntity)
            || !AreEnemies(command.PlayerId, World.Get<Owner>(command.TargetEntity).PlayerId))
        {
            return false;
        }

        SetOrder(command.Unit, new UnitOrder(OrderKind.Attack, command.TargetEntity, FixVec2.Zero));
        World.Get<Armament>(command.Unit).Target = command.TargetEntity;
        return true;
    }

    private bool ApplyAttackMove(in Command command)
    {
        if (!OwnsLiveUnit(command) || !World.Has<Armament>(command.Unit))
        {
            return false;
        }

        SetOrder(command.Unit, new UnitOrder(OrderKind.AttackMove, Entity.None, command.Target));
        ref var mover = ref World.Get<Mover>(command.Unit);
        Navigator.PlanRoute(command.Unit.Index, World.Get<Position>(command.Unit).Value, command.Target, ref mover);
        return true;
    }

    private bool ApplyBuild(in Command command)
    {
        if (CheckPlacement(command.PlayerId, command.TypeIndex, command.Cell) != PlacementResult.Ok)
        {
            return false;
        }

        var player = _players[command.PlayerId];
        player.Spend(Content.Structure(command.TypeIndex).Cost);
        var site = PlaceStructure(command.PlayerId, command.TypeIndex, command.Cell, complete: false);
        if (Content.Structure(command.TypeIndex).Role != StructureRole.Wall)
        {
            player.ConstructionSite = site; // walls build alongside everything else and never take the slot
        }
        return true;
    }

    private bool ApplyTrain(in Command command)
    {
        if (command.PlayerId < 0 || command.PlayerId >= _players.Length
            || command.TypeIndex < 0 || command.TypeIndex >= Content.Units.Count
            || !World.IsAlive(command.Unit)
            || !World.Has<Producer>(command.Unit)
            || World.Get<Owner>(command.Unit).PlayerId != command.PlayerId
            || !World.Get<StructureInfo>(command.Unit).IsComplete)
        {
            return false;
        }

        var player = _players[command.PlayerId];
        var unit = Content.Unit(command.TypeIndex);
        var producerRole = Content.Structure(World.Get<StructureInfo>(command.Unit).StructureType).Role;
        ref var producer = ref World.Get<Producer>(command.Unit);
        if (player.Defeated
            || unit.ProducedBy != producerRole
            || !Content.Faction(player.Faction).UnitIds.Contains(unit.Id)
            || producer.IsFull
            || !player.CanAfford(unit.Cost))
        {
            return false;
        }

        player.Spend(unit.Cost);
        if (producer.IsIdle)
        {
            producer.Remaining = unit.BuildTicks;
        }

        producer.TryEnqueue(command.TypeIndex);
        return true;
    }

    private void SetOrder(Entity unit, UnitOrder order)
    {
        if (World.Has<UnitOrder>(unit))
        {
            World.Get<UnitOrder>(unit) = order;
        }
    }

    private bool IsFootprintFree(Cell origin, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!Map.IsBuildable(new Cell(origin.X + x, origin.Y + y)))
                {
                    return false;
                }
            }
        }

        // No unit may be standing where the structure goes.
        var minX = Fix64.FromInt(origin.X);
        var minY = Fix64.FromInt(origin.Y);
        var maxX = minX + Fix64.FromInt(width);
        var maxY = minY + Fix64.FromInt(height);
        var positions = World.Store<Position>();
        var indices = positions.EntityIndices;
        var values = positions.Components;
        var structures = World.Store<StructureInfo>();
        for (var i = 0; i < values.Length; i++)
        {
            var point = values[i].Value;
            if (!structures.Has(indices[i])
                && point.X >= minX && point.X < maxX && point.Y >= minY && point.Y < maxY)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsNearOwnStructure(int playerId, Cell origin, int width, int height)
    {
        var radius = Content.Economy.BuildRadius;
        var structures = World.Store<StructureInfo>();
        var owners = World.Store<Owner>();
        var indices = structures.EntityIndices;
        var infos = structures.Components;
        for (var i = 0; i < infos.Length; i++)
        {
            var other = Content.Structure(infos[i].StructureType);
            if (owners.Get(indices[i]).PlayerId != playerId || other.Role == StructureRole.Wall)
            {
                continue; // walls hold a line; they do not stretch the base
            }

            var gapX = Gap(origin.X, width, infos[i].Origin.X, other.Width);
            var gapY = Gap(origin.Y, height, infos[i].Origin.Y, other.Height);
            if (gapX <= radius && gapY <= radius)
            {
                return true;
            }
        }

        return false;
    }

    // Empty cells between two spans on one axis (0 if they touch or overlap).
    private static int Gap(int startA, int lengthA, int startB, int lengthB)
    {
        if (startA + lengthA <= startB)
        {
            return startB - (startA + lengthA);
        }

        return startB + lengthB <= startA ? startA - (startB + lengthB) : 0;
    }

    // Stable insertion sort by player id: within one player, the order the player issued commands is kept.
    // Lockstep peers may receive different players' turns in different orders, so this makes them agree.
    private ReadOnlySpan<Command> SortByPlayer(ReadOnlySpan<Command> commands)
    {
        if (commands.Length > _commandScratch.Length)
        {
            _commandScratch = new Command[commands.Length * 2];
        }

        var sorted = _commandScratch.AsSpan(0, commands.Length);
        commands.CopyTo(sorted);
        for (var i = 1; i < sorted.Length; i++)
        {
            var current = sorted[i];
            var j = i - 1;
            while (j >= 0 && sorted[j].PlayerId > current.PlayerId)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }

            sorted[j + 1] = current;
        }

        return sorted;
    }
}
