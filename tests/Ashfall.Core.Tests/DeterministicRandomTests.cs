namespace Ashfall.Core.Tests;

public class DeterministicRandomTests
{
    [Fact]
    public void Seed_42_produces_the_pinned_sequence()
    {
        // Pinned output: if this changes, every replay and golden hash changes with it.
        var random = new DeterministicRandom(42);
        uint[] expected = [1737885888u, 3874661343u, 733085944u, 213828297u, 751268722u];

        foreach (var value in expected)
        {
            Assert.Equal(value, random.NextUInt32());
        }
    }

    [Fact]
    public void Streams_from_one_seed_are_independent_and_pinned()
    {
        var stream7 = new DeterministicRandom(42, stream: 7);
        uint[] expected = [2934674539u, 1908077436u, 4080299266u];

        foreach (var value in expected)
        {
            Assert.Equal(value, stream7.NextUInt32());
        }
    }

    [Fact]
    public void Same_seed_gives_the_same_sequence()
    {
        var a = new DeterministicRandom(123_456);
        var b = new DeterministicRandom(123_456);

        for (var i = 0; i < 1_000; i++)
        {
            Assert.Equal(a.NextUInt32(), b.NextUInt32());
        }
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var a = new DeterministicRandom(1);
        var b = new DeterministicRandom(2);
        var differences = 0;
        for (var i = 0; i < 100; i++)
        {
            if (a.NextUInt32() != b.NextUInt32())
            {
                differences++;
            }
        }

        Assert.InRange(differences, 95, 100);
    }

    [Fact]
    public void Restoring_a_snapshot_continues_the_sequence()
    {
        var original = new DeterministicRandom(99);
        original.NextUInt32();
        original.NextUInt32();

        var restored = DeterministicRandom.Restore(original.Snapshot);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(original.NextUInt32(), restored.NextUInt32());
        }
    }

    [Fact]
    public void Bounded_values_stay_in_range_and_are_roughly_uniform()
    {
        var random = new DeterministicRandom(7);
        var buckets = new int[6];
        for (var i = 0; i < 60_000; i++)
        {
            buckets[random.Next(6)]++;
        }

        foreach (var count in buckets)
        {
            Assert.InRange(count, 9_500, 10_500);
        }

        for (var i = 0; i < 1_000; i++)
        {
            Assert.InRange(random.Next(-5, 5), -5, 4);
        }
    }

    [Fact]
    public void Fixed_point_values_stay_in_range()
    {
        var random = new DeterministicRandom(11);
        var min = Fix64.FromInt(-2);
        var max = Fix64.FromInt(3);
        for (var i = 0; i < 1_000; i++)
        {
            var value = random.NextFix64(min, max);
            Assert.True(value >= min && value < max, $"{value} outside [{min}, {max})");
        }
    }

    [Fact]
    public void Invalid_bounds_throw()
    {
        var random = new DeterministicRandom(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(5, 5));
    }
}
