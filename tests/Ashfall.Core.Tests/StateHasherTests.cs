namespace Ashfall.Core.Tests;

public class StateHasherTests
{
    [Fact]
    public void Empty_hasher_returns_the_FNV_offset_basis()
    {
        Assert.Equal(14_695_981_039_346_656_037UL, new StateHasher().Value);
    }

    [Fact]
    public void Matches_reference_FNV_1a_values()
    {
        // Reference values computed independently over the same little-endian bytes.
        var zero = new StateHasher();
        zero.Add(0UL);
        var counting = new StateHasher();
        counting.Add(0x0102030405060708UL);

        Assert.Equal(0xA8C7F832281A39C5UL, zero.Value);
        Assert.Equal(0x0C6D4496E17859D5UL, counting.Value);
    }

    [Fact]
    public void Order_of_values_matters()
    {
        var first = new StateHasher();
        first.Add(1);
        first.Add(2);
        var second = new StateHasher();
        second.Add(2);
        second.Add(1);

        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Fixed_point_values_hash_by_raw_bits()
    {
        var viaFix = new StateHasher();
        viaFix.Add(Fix64.Half);
        var viaRaw = new StateHasher();
        viaRaw.Add(Fix64.Half.RawValue);

        Assert.Equal(viaRaw.Value, viaFix.Value);
    }

    [Fact]
    public void Format_prints_sixteen_hex_digits()
    {
        Assert.Equal("0x00000000000000FF", StateHasher.Format(255));
    }
}
