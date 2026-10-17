using Ashfall.Core;
using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Tests;

public class WorldTests
{
    [Fact]
    public void Entities_get_sequential_indices()
    {
        var world = NewWorld();

        var first = world.Create();
        var second = world.Create();

        Assert.Equal(new Entity(0, 0), first);
        Assert.Equal(new Entity(1, 0), second);
        Assert.Equal(2, world.AliveCount);
    }

    [Fact]
    public void Destruction_is_deferred_until_flush()
    {
        var world = NewWorld();
        var entity = world.Create();
        world.Add(entity, new Owner(1));

        world.Destroy(entity);
        Assert.True(world.IsAlive(entity));
        Assert.True(world.Has<Owner>(entity));

        world.FlushDestroyed();
        Assert.False(world.IsAlive(entity));
        Assert.Equal(0, world.AliveCount);
        Assert.Equal(0, world.Store<Owner>().Count);
    }

    [Fact]
    public void Destroying_twice_is_harmless()
    {
        var world = NewWorld();
        var entity = world.Create();

        world.Destroy(entity);
        world.Destroy(entity);
        world.FlushDestroyed();
        world.Destroy(entity);
        world.FlushDestroyed();

        Assert.Equal(0, world.AliveCount);
    }

    [Fact]
    public void Freed_indices_are_reused_lowest_first_with_a_new_generation()
    {
        var world = NewWorld();
        var a = world.Create();
        var b = world.Create();
        world.Create();

        world.Destroy(b);
        world.Destroy(a);
        world.FlushDestroyed();

        var reusedFirst = world.Create();
        var reusedSecond = world.Create();

        Assert.Equal(new Entity(0, 1), reusedFirst);
        Assert.Equal(new Entity(1, 1), reusedSecond);
        Assert.False(world.IsAlive(a));
        Assert.True(world.IsAlive(reusedFirst));
    }

    [Fact]
    public void Stale_handles_cannot_reach_a_reused_slot()
    {
        var world = NewWorld();
        var stale = world.Create();
        world.Destroy(stale);
        world.FlushDestroyed();
        var fresh = world.Create();
        world.Add(fresh, new Owner(3));

        Assert.False(world.Has<Owner>(stale));
        Assert.Throws<InvalidOperationException>(() => world.Add(stale, new Owner(4)));
        Assert.Equal(3, world.Get<Owner>(fresh).PlayerId);
    }

    [Fact]
    public void Registration_rules_are_enforced()
    {
        var world = NewWorld();

        Assert.Throws<InvalidOperationException>(() => world.Register<Owner>());
        Assert.Throws<InvalidOperationException>(() => new World().Store<Owner>());
    }

    [Fact]
    public void State_hash_is_canonical_regardless_of_insertion_order()
    {
        // Same final state, components added in a different order: the hash must not care.
        var a = NewWorld();
        var a0 = a.Create();
        var a1 = a.Create();
        a.Add(a1, new Owner(2));
        a.Add(a0, new Owner(1));
        a.Add(a0, new Position(FixVec2.FromInts(1, 1)));

        var b = NewWorld();
        var b0 = b.Create();
        var b1 = b.Create();
        b.Add(b0, new Position(FixVec2.FromInts(1, 1)));
        b.Add(b0, new Owner(1));
        b.Add(b1, new Owner(2));

        Assert.Equal(Hash(a), Hash(b));
    }

    [Fact]
    public void State_hash_changes_when_a_component_changes()
    {
        var world = NewWorld();
        var entity = world.Create();
        world.Add(entity, new Position(FixVec2.FromInts(1, 1)));
        var before = Hash(world);

        world.Get<Position>(entity).Value = FixVec2.FromInts(1, 2);

        Assert.NotEqual(before, Hash(world));
    }

    private static World NewWorld()
    {
        var world = new World();
        world.Register<Position>();
        world.Register<Owner>();
        return world;
    }

    private static ulong Hash(World world)
    {
        var hasher = new StateHasher();
        world.HashInto(ref hasher);
        return hasher.Value;
    }
}
