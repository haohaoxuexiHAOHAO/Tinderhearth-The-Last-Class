using System.Text.Json;
using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// 作物定义的载入校验：缺字段、天数不足、季节列表为空、退回成熟那一阶段，都要当场报错。
/// </summary>
/// <remarks>
/// 玩法数值这里一个都不测 —— 阶段天数与产量归设计仓 design/数值模型.md，现在还没有值。
/// </remarks>
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

    /// <summary>JSON 里缺任何一个键都要报错。</summary>
    /// <remarks>
    /// 这一条就是作物定义用 <c>required</c> 属性而不用位置参数 record 的全部理由：实测
    /// 位置参数配 <c>System.Text.Json</c> 时缺字段会拿 <c>default</c> 填，而
    /// <see cref="CropDefinition.RegrowFromStage"/> 的 <c>null</c> 本身是合法值，
    /// 所以缺键与显式写 null 靠构造校验区分不出来。
    /// </remarks>
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

    /// <summary>计时阶段一个都没有就被拒 —— 那样的作物播下去当天就待收。</summary>
    [Fact]
    public void 计时阶段一个都没有就被拒()
    {
        Assert.Throws<ArgumentException>(() => Crop(stageDays: []));
    }

    /// <summary>阶段数由每条作物自己声明，所以项数多少都成立，最少一项。</summary>
    /// <remarks>
    /// 它与「计时阶段一个都没有就被拒」合起来说明校验判的是「至少一项」，不是「恰好几项」。
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void 阶段数由作物自己声明(int timedStages)
    {
        var crop = Crop(stageDays: [.. Enumerable.Repeat(1, timedStages)]);

        Assert.Equal(timedStages, crop.StageDays.Count);
        Assert.Equal(timedStages, crop.RipeStage);
        Assert.Equal(timedStages + 1, crop.StageCount);
        Assert.Equal(timedStages, crop.DaysToFirstRipe);
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

    /// <summary>季节列表为空就被拒。</summary>
    /// <remarks>
    /// 空列表意味着任何一次换季都让它枯死，也就是种下去撑不过当季 ——
    /// 而那看起来像玩法缺陷，不像数据填错。
    /// </remarks>
    [Fact]
    public void 季节列表为空就被拒()
    {
        Assert.Throws<ArgumentException>(() => Crop(seasons: []));
    }

    /// <summary>负的退回阶段被拒 —— 生长会往回走。这一条不跨字段，所以属性赋值时就判得到。</summary>
    [Fact]
    public void 退回的阶段为负就被拒()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Crop(regrowFromStage: -1));
    }

    /// <summary>退回成熟那一阶段或更后被拒：退回去之后立刻又是待收，一次收获就能无限收下去。</summary>
    /// <remarks>
    /// 上界要读 <c>RipeStage</c>，而它由阶段数算出来，跨字段的校验落在 <c>Parse</c> 上，
    /// 所以这一条走 JSON 而不是对象初始化器。
    /// </remarks>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 4)]
    public void 退回成熟那一阶段或更后就被拒(int timedStages, int regrowFrom)
    {
        var days = string.Join(", ", Enumerable.Repeat(1, timedStages));
        var json = $$"""
            {
              "id": "turnip",
              "stageDays": [{{days}}],
              "regrowFromStage": {{regrowFrom}},
              "seasons": ["Spring"],
              "yieldItemId": "item_turnip"
            }
            """;

        Assert.Throws<ArgumentOutOfRangeException>(() => CropDefinition.Parse(json, "turnip.json"));
    }

    /// <summary>退回成熟之前的阶段照旧成立。</summary>
    /// <remarks>缺了这条反证，就分不出上一条校验判的是上界，还是凡是循环收获都拒。</remarks>
    [Fact]
    public void 退回成熟之前的阶段照旧成立()
    {
        var json = """
            {
              "id": "turnip",
              "stageDays": [1, 1],
              "regrowFromStage": 1,
              "seasons": ["Spring"],
              "yieldItemId": "item_turnip"
            }
            """;

        var crop = CropDefinition.Parse(json, "turnip.json");

        Assert.True(crop.Regrows);
        Assert.Equal(1, crop.RegrowFromStage);
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

    /// <summary>再生要几天由退回的那一阶段算出来，不另给一个字段。</summary>
    /// <remarks>
    /// 它就是从退回那一阶段起剩下那几项天数之和。多一个字段就多一处要与这份天数对账的地方。
    /// </remarks>
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

    /// <summary>季节按枚举名字读，不按序号；名字写错是解析失败。</summary>
    /// <remarks>
    /// 序号在手写数据里读不出含义，而且往枚举中间插一个值会静默改掉全部旧数据的含义。
    /// 名字写错至少会解析失败，那是查得出来的。
    /// </remarks>
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
