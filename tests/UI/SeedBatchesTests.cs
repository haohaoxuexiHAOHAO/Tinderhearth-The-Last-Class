using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>随身栏接上背包之前的种子批：按标识排、每批装满、不足补空格，最后一批之后回到第一批。</summary>
/// <remarks>格数从随身栏取，这里不写死一个数：改了格数，这几条照样成立。</remarks>
public class SeedBatchesTests
{
    private static readonly int Slots = CarrySlotBar.SlotCount;

    /// <summary>补零的标识，按序号排就是按字符排。</summary>
    private static List<string> Seeds(int count) => [.. Enumerable.Range(1, count).Select(n => $"crop_{n:00}")];

    [Fact]
    public void 按标识排序后每批装满一条随身栏()
    {
        var seeds = Seeds((Slots * 2) + 1);
        var shuffled = seeds.AsEnumerable().Reverse();

        var first = SeedBatches.Batch(shuffled, 0);

        Assert.Equal(seeds.Take(Slots), first.Select(s => s.ItemId));
        Assert.All(first, s => Assert.Equal(1, s.Count));
    }

    [Fact]
    public void 最后一批不足时其余格留空()
    {
        var seeds = Seeds((Slots * 2) + 1);

        var last = SeedBatches.Batch(seeds, 2);

        Assert.Equal(seeds[^1], last[0].ItemId);
        Assert.All(last.Skip(1), s => Assert.True(s.IsEmpty));
        Assert.Equal(Slots, last.Count);
    }

    /// <summary>换批挨批往后走，最后一批之后回到第一批；每一种都恰好落在某一批里一次。</summary>
    [Fact]
    public void 换批一圈之后回到第一批且每种都出现一次()
    {
        var seeds = Seeds((Slots * 2) + 1);
        var seen = new List<string>();
        var batch = 0;

        for (var press = 0; press < SeedBatches.CountFor(seeds.Count); press++)
        {
            seen.AddRange(SeedBatches.Batch(seeds, batch).Where(s => !s.IsEmpty).Select(s => s.ItemId));
            batch = SeedBatches.Next(batch, seeds.Count);
        }

        Assert.Equal(0, batch);
        Assert.Equal(seeds, seen);
    }

    /// <summary>一种都没有、或正好装满一条时只有一批，换批停在原地。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 一批装得下时只有一批而换批停在原地(bool full)
    {
        var seedCount = full ? Slots : 0;

        Assert.Equal(1, SeedBatches.CountFor(seedCount));
        Assert.Equal(0, SeedBatches.Next(0, seedCount));
    }

    /// <summary>批号越界当场报错，不静默绕回：绕回只由换批那一处负责。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void 批号越界当场报错(int batch)
    {
        var seeds = Seeds((Slots * 2) + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => SeedBatches.Batch(seeds, batch));
    }
}
