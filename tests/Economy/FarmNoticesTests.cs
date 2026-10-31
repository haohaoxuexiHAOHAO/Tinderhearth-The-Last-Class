using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>农场里被拒的那一下，即时提示那一行写哪一句。</summary>
/// <remarks>
/// 挑哪一句用一份假文本表测，每一条一眼认得出是哪个键；措辞不在这里钉，作者改字不该让这里红。
/// 真文本表只核一件事：要的那几条一条不缺。
/// </remarks>
public class FarmNoticesTests
{
    private static readonly TextCatalog Fake = new(FarmNotices.TextKeys.ToDictionary(key => key, key => $"<{key}>"));

    [Fact]
    public void 文本表里有每一种被拒要的那一句()
    {
        var path = Path.Combine(RepoFiles.Root(), "data", "text", "zh-CN.json");
        var table = new TextCatalog(ContentJson.Parse<Dictionary<string, string>>(File.ReadAllText(path), path));

        Assert.Empty(table.MissingKeys(FarmNotices.TextKeys));
    }

    [Fact]
    public void 播下去了不出提示()
    {
        Assert.Null(FarmNotices.ForSowing(PlantResult.Planted, Fake));
    }

    [Fact]
    public void 播种被拒的每一种各是一句而且彼此不同()
    {
        // 合成一句「不能播」之后，玩家不知道该换格子、换东西还是先锄地。
        var rejected = Enum.GetValues<PlantResult>().Where(r => r != PlantResult.Planted).ToList();

        var lines = rejected.Select(r => FarmNotices.ForSowing(r, Fake)).ToList();

        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
        Assert.Equal(rejected.Count, lines.Distinct().Count());
        Assert.Equal($"<{FarmNotices.SowNotASeedKey}>", FarmNotices.ForSowing(PlantResult.NotASeed, Fake));
    }

    [Fact]
    public void 收获只在背包放不下时出提示()
    {
        // 不是待收的那一格到不了收获，那一下不是被拒，不出提示。
        Assert.Null(FarmNotices.ForHarvest(HarvestStatus.Stored, Fake));
        Assert.Null(FarmNotices.ForHarvest(HarvestStatus.NotHarvestable, Fake));
        Assert.Equal($"<{FarmNotices.HarvestBagFullKey}>", FarmNotices.ForHarvest(HarvestStatus.BagFull, Fake));
    }
}
