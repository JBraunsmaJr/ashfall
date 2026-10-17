using Ashfall.Simulation.Components;
using Ashfall.Simulation.Ecs;

namespace Ashfall.Simulation.Tests;

public class ComponentStoreTests
{
    [Fact]
    public void Removing_from_the_middle_keeps_every_other_mapping_correct()
    {
        var store = new ComponentStore<Owner>();
        for (var index = 0; index < 5; index++)
        {
            store.Set(index, new Owner(index * 10));
        }

        Assert.True(store.Remove(1));

        Assert.Equal(4, store.Count);
        Assert.False(store.Has(1));
        foreach (var index in new[] { 0, 2, 3, 4 })
        {
            Assert.Equal(index * 10, store.Get(index).PlayerId);
        }
    }

    [Fact]
    public void Set_replaces_an_existing_component()
    {
        var store = new ComponentStore<Owner>();
        store.Set(7, new Owner(1));
        store.Set(7, new Owner(2));

        Assert.Equal(1, store.Count);
        Assert.Equal(2, store.Get(7).PlayerId);
    }

    [Fact]
    public void Store_grows_past_its_initial_capacity()
    {
        var store = new ComponentStore<Owner>();
        for (var index = 0; index < 1_000; index += 3)
        {
            store.Set(index, new Owner(index));
        }

        Assert.Equal(334, store.Count);
        Assert.Equal(999, store.Get(999).PlayerId);
    }

    [Fact]
    public void Missing_components_are_reported()
    {
        var store = new ComponentStore<Owner>();

        Assert.False(store.Has(3));
        Assert.False(store.Remove(3));
        Assert.Throws<InvalidOperationException>(() => store.Get(3));
    }
}
