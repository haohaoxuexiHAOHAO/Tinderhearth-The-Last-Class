using System.Text.Json;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Foundation;
using Tinderhearth.Rules.Tests.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>
/// 作物定义的载入校验：缺字段、天数不足、季节列表为空、退回成熟那一阶段，都要当场报错；种子与产出那两格要指到在册的物品，
/// 品质那一位要对。
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
          "seedItemId": "item_turnip_seed",
          "yieldItemId": "item_turnip"
        }
        """;

    private static CropDefinition Crop(
        int? regrowFromStage = null,
        IReadOnlyList<int>? stageDays = null,
        IReadOnlyList<Season>? seasons = null,
        string id = "turnip",
        string seedItemId = "item_turnip_seed",
        string yieldItemId = "item_turnip") => new()
        {
            Id = id,
            StageDays = stageDays ?? [1, 1, 1, 1],
            RegrowFromStage = regrowFromStage,
            Seasons = seasons ?? [Season.Spring],
            SeedItemId = seedItemId,
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
        Assert.Equal("item_turnip_seed", crop.SeedItemId);
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
    [InlineData("seedItemId")]
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
              "seedItemId": "item_turnip_seed",
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
              "seedItemId": "item_turnip_seed",
              "yieldItemId": "item_turnip"
            }
            """;

        var crop = CropDefinition.Parse(json, "turnip.json");

        Assert.True(crop.Regrows);
        Assert.Equal(1, crop.RegrowFromStage);
    }

    /// <summary>空白标识被拒 —— 它同时是查定义、播种与入库时的键，空了之后几处都找不到东西。</summary>
    [Theory]
    [InlineData("", "item_turnip_seed", "item_turnip")]
    [InlineData("   ", "item_turnip_seed", "item_turnip")]
    [InlineData("turnip", "", "item_turnip")]
    [InlineData("turnip", "  ", "item_turnip")]
    [InlineData("turnip", "item_turnip_seed", "")]
    [InlineData("turnip", "item_turnip_seed", "  ")]
    public void 空白标识被拒(string id, string seedItemId, string yieldItemId)
    {
        Assert.Throws<ArgumentException>(() => Crop(id: id, seedItemId: seedItemId, yieldItemId: yieldItemId));
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

    /// <summary>读全部作物定义时按标识建表，键是定义里写的标识，不是文件名。</summary>
    [Fact]
    public void 全部作物定义按标识建表()
    {
        var crops = CropDefinition.LoadAll(
            Catalog(
                ("crops/a.json", FullJson.Replace("\"turnip\"", "\"crop_02\"").Replace("item_turnip_seed", "item_seed_02")),
                ("crops/b.json", FullJson.Replace("\"turnip\"", "\"crop_01\"").Replace("item_turnip_seed", "item_seed_01"))),
            Items(("item_seed_01", false), ("item_seed_02", false), ("item_turnip", true)));

        Assert.Equal(["crop_01", "crop_02"], crops.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([1, 2, 2, 3], crops["crop_01"].StageDays);
    }

    /// <summary>一份种子只长一种作物：两种作物用了同一份种子，载入被拒并点名那两种与那一份种子。</summary>
    /// <remarks>不拦的话，手上拿着那一份种子播下去是哪一种取决于字典的先后，而那不报错。</remarks>
    [Fact]
    public void 两种作物用了同一份种子就被拒并点名()
    {
        var catalog = Catalog(
            ("crops/a.json", FullJson.Replace("\"turnip\"", "\"crop_02\"")),
            ("crops/b.json", FullJson.Replace("\"turnip\"", "\"crop_01\"")));

        var error = Assert.Throws<InvalidDataException>(
            () => CropDefinition.LoadAll(catalog, Items(("item_turnip_seed", false), ("item_turnip", true))));

        Assert.Contains("作物「crop_01」「crop_02」用的是同一份种子「item_turnip_seed」", error.Message);
    }

    /// <summary>按种子找作物：手上拿的是哪一份种子，查到的就是哪一种作物。</summary>
    [Fact]
    public void 按种子那一样物品找得到它的作物()
    {
        var turnip = Crop();
        var radish = Crop(id: "radish", seedItemId: "item_radish_seed", yieldItemId: "item_radish");

        var bySeed = CropDefinition.BySeed(new Dictionary<string, CropDefinition> { [turnip.Id] = turnip, [radish.Id] = radish });

        Assert.Same(radish, bySeed["item_radish_seed"]);
        Assert.Same(turnip, bySeed["item_turnip_seed"]);
        Assert.False(bySeed.ContainsKey("item_turnip"));
    }

    /// <summary>作物目录里一份都没有就抛，别的目录里有内容也不算。</summary>
    /// <remarks>没有一种作物的农场随身栏是空的，而那不报错 —— 表现只是种不了地。</remarks>
    [Fact]
    public void 一份作物定义都没有就抛()
    {
        var catalog = Catalog(("characters/jin.json", """{ "id": "jin", "displayNameKey": "character.jin.name", "traitIds": [] }"""));

        var error = Assert.Throws<InvalidDataException>(() => CropDefinition.LoadAll(catalog, Items()));

        Assert.Contains($"{CropDefinition.ContentDirectory}/", error.Message);
    }

    /// <summary>种子或产出指不到在册的物品，载入被拒，点名是哪种作物的哪一格；两格都错就一次报两句。</summary>
    [Fact]
    public void 种子或产出指不到在册物品就被拒并点名()
    {
        var catalog = Catalog(("crops/turnip.json", FullJson));

        var error = Assert.Throws<InvalidDataException>(() => CropDefinition.LoadAll(catalog, Items(("unrelated", true))));

        Assert.Contains("作物「turnip」的 SeedItemId「item_turnip_seed」不在册", error.Message);
        Assert.Contains("作物「turnip」的 YieldItemId「item_turnip」不在册", error.Message);
    }

    /// <summary>种子要声明无品质、产出要声明有品质，反了就被拒。</summary>
    /// <remarks>
    /// 种子没有做得好坏之分；产出那一样要装得下变了的那一株收上来的变异品质（设计仓
    /// decisions/ADR-0030-作物成熟时按几率变异.md）。
    /// </remarks>
    [Fact]
    public void 种子有品质或产出无品质就被拒()
    {
        var catalog = Catalog(("crops/turnip.json", FullJson));

        var error = Assert.Throws<InvalidDataException>(
            () => CropDefinition.LoadAll(catalog, Items(("item_turnip_seed", true), ("item_turnip", false))));

        Assert.Contains("「item_turnip_seed」声明了有品质", error.Message);
        Assert.Contains("「item_turnip」声明了无品质", error.Message);
    }

    /// <summary>作物叫什么读它产出的那一样物品，作物自己没有名字那一条。</summary>
    [Fact]
    public void 作物的名字读它产出的那一样物品()
    {
        var items = Items(("item_turnip_seed", false), ("item_turnip", true));

        Assert.Equal(items.Items["item_turnip"].NameKey, Crop().NameKeyIn(items));
    }

    /// <summary>工程里每种作物都有一份种子与一份收获物：种子无品质、归种子那一类，收获物有品质。</summary>
    [Fact]
    public void 工程里每种作物都有一份种子与一份收获物()
    {
        var items = ShippedItems.Load();
        var crops = CropSheetE2ETests.ShippedCrops();

        Assert.NotEmpty(crops);
        Assert.All(crops.Values, crop =>
        {
            Assert.False(items.Items[crop.SeedItemId].HasQuality);
            Assert.Equal("seed", items.Items[crop.SeedItemId].CategoryId);
            Assert.True(items.Items[crop.YieldItemId].HasQuality);
        });

        // 一种作物一份：没有两种作物共用一样种子或一样收获物，也没有哪一样既是种子又是收获物。
        var referenced = crops.Values.SelectMany(c => new[] { c.SeedItemId, c.YieldItemId }).ToList();
        Assert.Equal(referenced.Count, referenced.Distinct(StringComparer.Ordinal).Count());
    }

    private static ContentCatalog Catalog(params (string Path, string Json)[] files)
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", files.ToDictionary(f => f.Path, f => f.Json)));
        return catalog;
    }

    /// <summary>一份只有给定那几样物品的物品表：一个类别、一档稀有度、一档品质，够作物这一侧核引用就行。</summary>
    private static ItemCatalog Items(params (string Id, bool HasQuality)[] items) =>
        ItemCatalog.Build(
            items.Select(i => new ItemDefinition
            {
                Id = i.Id,
                NameKey = $"item.{i.Id}.name",
                DescriptionKey = $"item.{i.Id}.description",
                CategoryId = "seed",
                RarityId = "low",
                HasQuality = i.HasQuality,
                StackLimit = 20,
                BasePriceCopper = null,
                Discardable = true,
                Sellable = true,
                UseEffectId = null,
                ConfirmBeforeUse = false,
            }),
            [new ItemCategory { Id = "seed", NameKey = "item_category.seed.name", ParentId = null }],
            [new ItemTier { Id = "low", NameKey = "item_rarity.low.name", SortOrder = 1, BasePriceFactor = null, PositiveAffixCount = 0, DisplayColor = "copper 3" }],
            [new ItemTier { Id = "common", NameKey = "item_quality.common.name", SortOrder = 2, BasePriceFactor = null, DisplayColor = "ash 6" }],
            name => name.Split(' ')[0]);

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
