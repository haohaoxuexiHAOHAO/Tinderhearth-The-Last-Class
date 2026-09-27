using System.Text.Json;
using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// `GP-87` 作物定义的载入校验。**不测任何玩法数值** —— 阶段天数与产量都归数值模型、现在没有值。
/// 这里钉的是「填错了要响着坏」：缺字段、天数不足、季节空、退回成熟那一阶段。
/// </summary>
public class CropDefinitionTests
{
    /// <summary>一份完整且合法的作物 JSON。各测试从它出发删或改一处。</summary>
    private const string FullJson = """
        {
          "id": "turnip",
          "stageDays": [1, 2, 2, 3],
          "regrowFromStage": null,
          "seasons": ["Spring", "Autumn"],
          "yieldItemId": "item_turnip"
        }
        """;

    private static CropDefinition Crop(
        int? regrowFromStage = null,
        IReadOnlyList<int>? stageDays = null,
        IReadOnlyList<Season>? seasons = null,
        string id = "turnip",
        string yieldItemId = "item_turnip") => new()
        {
            Id = id,
            StageDays = stageDays ?? [1, 1, 1, 1],
            RegrowFromStage = regrowFromStage,
            Seasons = seasons ?? [Season.Spring],
            YieldItemId = yieldItemId,
        };

    /// <summary>先证明那份 JSON 本身是好的，否则下面每一条「删一个键就失败」都可能是假绿。</summary>
    [Fact]
    public void 完整的作物定义解析得出来()
    {
        var crop = CropDefinition.Parse(FullJson, "turnip.json");
        Assert.Equal("turnip", crop.Id);
        Assert.Equal([1, 2, 2, 3], crop.StageDays);
        Assert.Equal([Season.Spring, Season.Autumn], crop.Seasons);
        Assert.Equal("item_turnip", crop.YieldItemId);
        Assert.False(crop.Regrows);
        Assert.Null(crop.DaysToRegrow);
    }

    /// <summary>
    /// **缺任一键都要报错。** 这一条是本类用 <c>required</c> 属性而不用位置参数 record 的全部
    /// 理由：位置参数配 <c>System.Text.Json</c> 时缺字段拿 <c>default</c>（`GameConfig` 实测过），
    /// 而 <see cref="CropDefinition.RegrowFromStage"/> 的 <c>null</c> **是合法值**，
    /// 所以「缺键」与「显式写 null」靠构造校验区分不出来。
    /// </summary>
    [Theory]
    [InlineData("id")]
    [InlineData("stageDays")]
    [InlineData("regrowFromStage")]
    [InlineData("seasons")]
    [InlineData("yieldItemId")]
    public void 缺任一键都在解析时报错(string keyToDrop)
    {
        var broken = WithoutKey(FullJson, keyToDrop);
        Assert.Throws<JsonException>(() => CropDefinition.Parse(broken, "broken.json"));
    }

    /// <summary>
    /// 天数的项数必须恰好等于要计时的阶段数。**成熟那一阶段不带天数** —— 它是终态，到了就一直
    /// 待收。多给一项会多出一个没有任何东西读它的字段，而那种字段填错了不报错。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void 阶段天数的项数不对就被拒(int count)
    {
        Assert.NotEqual(CropDefinition.TimedStageCount, count);
        Assert.Throws<ArgumentException>(() => Crop(stageDays: [.. Enumerable.Repeat(1, count)]));
    }

    /// <summary>
    /// 任一阶段少于 1 天被拒。0 天的阶段会让那一阶段的素材一天也显示不到，而它不报错 ——
    /// 表现只是「那张图好像没用上」。负数更糟：生长会往回走。
    /// </summary>
    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 1, 0)]
    [InlineData(1, -3, 1, 1)]
    public void 任一阶段少于一天就被拒(int a, int b, int c, int d)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Crop(stageDays: [a, b, c, d]));
    }

    /// <summary>
    /// **季节列表不许为空**：空列表意味着任何一次换季都让它枯死，也就是种下去撑不过当季 ——
    /// 而那看起来像玩法缺陷，不像数据填错。
    /// </summary>
    [Fact]
    public void 季节列表为空就被拒()
    {
        Assert.Throws<ArgumentException>(() => Crop(seasons: []));
    }

    /// <summary>
    /// **退回成熟那一阶段被拒**：退回去之后立刻又是待收，一次收获就能无限收下去。负数同拒。
    /// </summary>
    [Theory]
    [InlineData(CropDefinition.RipeStage)]
    [InlineData(CropDefinition.RipeStage + 1)]
    [InlineData(-1)]
    public void 退回的阶段超出范围就被拒(int stage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Crop(regrowFromStage: stage));
    }

    /// <summary>空白标识被拒 —— 它同时是查定义与入库时的键，空了之后两处都找不到东西。</summary>
    [Theory]
    [InlineData("", "item_turnip")]
    [InlineData("   ", "item_turnip")]
    [InlineData("turnip", "")]
    [InlineData("turnip", "  ")]
    public void 空白标识被拒(string id, string yieldItemId)
    {
        Assert.Throws<ArgumentException>(() => Crop(id: id, yieldItemId: yieldItemId));
    }

    /// <summary>
    /// 循环收获的两个派生量算得对：**再生间隔没有单独的字段**，它就是从退回那一阶段起
    /// 剩下那几项天数之和。多一个字段就多一处要与这份天数对账的地方。
    /// </summary>
    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 7)]
    [InlineData(2, 5)]
    [InlineData(3, 3)]
    public void 再生要几天由退回的阶段算出来而不另给一个数(int back, int expectedRegrowDays)
    {
        var crop = Crop(regrowFromStage: back, stageDays: [1, 2, 2, 3]);
        Assert.True(crop.Regrows);
        Assert.Equal(8, crop.DaysToFirstRipe);
        Assert.Equal(expectedRegrowDays, crop.DaysToRegrow);
    }

    /// <summary>
    /// 枚举**按名字读**，不按序号。序号在手写数据里读不出含义，而且往枚举中间插一个值会静默
    /// 改掉全部旧数据的含义。名字写错则是解析失败，那是查得出来的。
    /// </summary>
    [Fact]
    public void 季节按名字读而写错名字是解析失败()
    {
        var crop = CropDefinition.Parse(FullJson, "turnip.json");
        Assert.Equal([Season.Spring, Season.Autumn], crop.Seasons);

        var typo = FullJson.Replace("\"Autumn\"", "\"Fall\"");
        Assert.Throws<JsonException>(() => CropDefinition.Parse(typo, "typo.json"));
    }

    /// <summary>从一份 JSON 里去掉一个顶层键，用来造「缺字段」的输入。</summary>
    private static string WithoutKey(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        var kept = document.RootElement
            .EnumerateObject()
            .Where(property => property.Name != key)
            .Select(property => $"\"{property.Name}\":{property.Value.GetRawText()}");
        return $"{{{string.Join(",", kept)}}}";
    }
}
