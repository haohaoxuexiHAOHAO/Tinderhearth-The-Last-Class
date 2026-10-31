using System.Text.Json;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Foundation;
using Tinderhearth.Rules.Tests.UI;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Progression;

/// <summary>
/// 物品定义、类别与两条轴的档位：缺一格被拒、名字写成中文串被拒、指不到在册的被拒、显示色不在色板里或撞色带被拒，
/// 多放一份档位文件不改代码照样读得进来。
/// </summary>
/// <remarks>
/// 规则在设计仓 design/物品系统.md 的「物品定义的字段：只写「它是什么」，不写「它能干什么」」与「两条轴的档位各是一张
/// 在册的声明表」两节。玩法数值这里一个都不测 —— 基础价与系数还没有值，堆上限是占位。
/// </remarks>
public class ItemCatalogTests
{
    /// <summary>一份完整且合法的物品 JSON。各测试从它出发删或改一处。</summary>
    private const string FullItemJson = """
        {
          "id": "turnip_seed",
          "nameKey": "item.turnip_seed.name",
          "descriptionKey": "item.turnip_seed.description",
          "categoryId": "seed",
          "rarityId": "low",
          "hasQuality": false,
          "stackLimit": 20,
          "basePriceCopper": null,
          "discardable": true,
          "sellable": true,
          "useEffectId": null,
          "confirmBeforeUse": false
        }
        """;

    /// <summary>一份完整且合法的稀有度 JSON。</summary>
    private const string FullRarityJson = """
        {
          "id": "low",
          "nameKey": "item_rarity.low.name",
          "sortOrder": 1,
          "basePriceFactor": null,
          "positiveAffixCount": 0,
          "displayColor": "copper 3"
        }
        """;

    /// <summary>一份完整且合法的品质 JSON。</summary>
    private const string FullQualityJson = """
        {
          "id": "common",
          "nameKey": "item_quality.common.name",
          "sortOrder": 2,
          "basePriceFactor": null,
          "displayColor": "ash 6"
        }
        """;

    /// <summary>测试用的一小份色板：几条色带各一两档，与仓库里那份无关，色板改了这里不跟着动。</summary>
    private static readonly Palette TestPalette = Palette.Parse(
        [
            "GIMP Palette",
            "#",
            "  1   1   1\tash 6",
            "  2   2   2\tember 4",
            "  3   3   3\tember 5",
            "  4   4   4\tspirit 3",
            "  5   5   5\tcopper 3",
            "  6   6   6\tice 4",
            "  7   7   7\tgold 5",
        ],
        "测试色板");

    // ── 物品定义 ──────────────────────────────────────────────────────

    /// <summary>先证明那份 JSON 本身是好的，否则下面每一条「删一个键就失败」都可能是假绿。</summary>
    [Fact]
    public void 完整的物品定义解析得出来()
    {
        var item = ItemDefinition.Parse(FullItemJson, "turnip_seed.json");

        Assert.Equal("turnip_seed", item.Id);
        Assert.Equal("seed", item.CategoryId);
        Assert.False(item.HasQuality);
        Assert.Equal(20, item.StackLimit);
        Assert.Null(item.BasePriceCopper);
        Assert.Null(item.UseEffectId);
        Assert.Equal(["item.turnip_seed.name", "item.turnip_seed.description"], item.TextKeys);
    }

    /// <summary>字段表里每一格都要写，缺一格解析时就报错。</summary>
    /// <remarks>
    /// 「有无品质」与「用掉前要不要一道确认」两格也在里面：它们的 <c>false</c> 是合法值，给默认值会让新加的一类静默落到
    /// 其中一边。「还没给值的价」与「没有使用效果」写显式的 <c>null</c>，不写这个键同样被拒。
    /// </remarks>
    [Theory]
    [InlineData("id")]
    [InlineData("nameKey")]
    [InlineData("descriptionKey")]
    [InlineData("categoryId")]
    [InlineData("rarityId")]
    [InlineData("hasQuality")]
    [InlineData("stackLimit")]
    [InlineData("basePriceCopper")]
    [InlineData("discardable")]
    [InlineData("sellable")]
    [InlineData("useEffectId")]
    [InlineData("confirmBeforeUse")]
    public void 物品定义缺任一格都在解析时报错(string keyToDrop)
    {
        var broken = WithoutKey(FullItemJson, keyToDrop);

        var error = Assert.Throws<JsonException>(() => ItemDefinition.Parse(broken, "broken.json"));

        Assert.Contains(keyToDrop, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>名称与描述写成中文串、或者不是键的写法，载入被拒，报错里点名是哪一份、哪一格。</summary>
    [Theory]
    [InlineData("nameKey", "萝卜种子")]
    [InlineData("nameKey", "turnip")]
    [InlineData("nameKey", "item.萝卜.name")]
    [InlineData("descriptionKey", "一种根菜的种子")]
    [InlineData("descriptionKey", "item.turnip seed.description")]
    public void 名称或描述不是文本键就被拒(string key, string value)
    {
        var catalog = Catalog(ShippedLikeTables(("items/turnip_seed.json", WithValue(FullItemJson, key, $"\"{value}\""))));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains("items/turnip_seed.json", error.Message);
        Assert.Contains(key, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>键的写法：几段字母、数字与下划线用点连起来。文本表里现有的几种写法都算。</summary>
    [Theory]
    [InlineData("item.crop_01_seed.name", true)]
    [InlineData("boot.contentReady", true)]
    [InlineData("ui.wristband.page.codex_crop", true)]
    [InlineData("name", false)]
    [InlineData("item..name", false)]
    [InlineData("1item.name", false)]
    [InlineData("作物.name", false)]
    [InlineData("", false)]
    public void 文本键的写法(string key, bool expected)
    {
        Assert.Equal(expected, TextKey.IsWellFormed(key));
    }

    /// <summary>堆上限必须为正；基础价要么还没给值、要么为正。</summary>
    [Theory]
    [InlineData("stackLimit", "0")]
    [InlineData("stackLimit", "-1")]
    [InlineData("basePriceCopper", "0")]
    [InlineData("basePriceCopper", "-5")]
    public void 堆上限与基础价填了不合理的数就被拒(string key, string value)
    {
        var catalog = Catalog(ShippedLikeTables(("items/turnip_seed.json", WithValue(FullItemJson, key, value))));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains(key, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>使用效果现在只许不填：效果那一侧还没有数据表，填了的引用解析不到。</summary>
    /// <remarks>放过它的话，「能用」的物品用下去什么都不发生，而那不报错。</remarks>
    [Fact]
    public void 使用效果现在只许是空的()
    {
        var catalog = Catalog(ShippedLikeTables(("items/turnip_seed.json", WithValue(FullItemJson, "useEffectId", "\"heal_small\""))));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains("heal_small", error.Message);
    }

    // ── 类别与引用 ────────────────────────────────────────────────────

    /// <summary>类别带层级：父类别接得上就读得进来，物品只存自己那一个类别。</summary>
    [Fact]
    public void 类别是一张带层级的表()
    {
        var items = ItemCatalog.Load(Catalog(ShippedLikeTables()), TestPalette.BandOf);

        Assert.Equal("material", items.Categories["herb"].ParentId);
        Assert.Null(items.Categories["material"].ParentId);
        Assert.Equal("seed", items.Items["turnip_seed"].CategoryId);
    }

    /// <summary>物品填的类别或稀有度不在册，载入被拒，点名是哪种物品的哪一格；两处一起错就一次报两句。</summary>
    [Fact]
    public void 类别或稀有度不在册就被拒并点名()
    {
        var broken = WithValue(WithValue(FullItemJson, "categoryId", "\"tool\""), "rarityId", "\"legendary\"");

        var error = Assert.Throws<InvalidDataException>(
            () => ItemCatalog.Load(Catalog(ShippedLikeTables(("items/turnip_seed.json", broken))), TestPalette.BandOf));

        Assert.Contains("物品「turnip_seed」的类别「tool」不在册", error.Message);
        Assert.Contains("物品「turnip_seed」的稀有度「legendary」不在册", error.Message);
    }

    /// <summary>父类别指不到、或者层级绕成一个圈，载入被拒。</summary>
    [Theory]
    [InlineData("\"tool\"", "上一层「tool」不在册")]
    [InlineData("\"herb\"", "绕回了")]
    public void 类别的层级接不上就被拒(string materialParent, string expected)
    {
        var catalog = Catalog(ShippedLikeTables(("item_categories/material.json", CategoryJson("material", materialParent))));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains(expected, error.Message);
    }

    /// <summary>两份文件用了同一个标识当场报错 —— 读法与作物定义同一套，后读到的那份不许静默顶掉前一份。</summary>
    [Fact]
    public void 两份物品用同一个标识就被拒()
    {
        var catalog = Catalog(ShippedLikeTables(("items/turnip_seed_copy.json", FullItemJson)));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains("items/turnip_seed.json", error.Message);
        Assert.Contains("items/turnip_seed_copy.json", error.Message);
    }

    // ── 档位 ──────────────────────────────────────────────────────────

    /// <summary>两条轴的档位每一项都要写，缺一项解析时就报错。还没给值的系数写显式的 null，不写这个键同样被拒。</summary>
    [Theory]
    [InlineData("Rarity", "id")]
    [InlineData("Rarity", "nameKey")]
    [InlineData("Rarity", "sortOrder")]
    [InlineData("Rarity", "basePriceFactor")]
    [InlineData("Rarity", "displayColor")]
    [InlineData("Quality", "id")]
    [InlineData("Quality", "nameKey")]
    [InlineData("Quality", "sortOrder")]
    [InlineData("Quality", "basePriceFactor")]
    [InlineData("Quality", "displayColor")]
    public void 档位缺任一声明项都在解析时报错(string axisName, string keyToDrop)
    {
        var axis = Enum.Parse<ItemTierAxis>(axisName);
        var full = axis == ItemTierAxis.Rarity ? FullRarityJson : FullQualityJson;

        var error = Assert.Throws<JsonException>(() => ItemTier.Parse(WithoutKey(full, keyToDrop), "broken.json", axis));

        Assert.Contains(keyToDrop, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>正面副词条几条只有稀有度那一轴声明：稀有度缺它被拒，品质填了它也被拒。</summary>
    [Fact]
    public void 正面副词条那一格按轴核()
    {
        var rarityMissing = Assert.Throws<InvalidDataException>(
            () => ItemTier.Parse(WithoutKey(FullRarityJson, "positiveAffixCount"), "low.json", ItemTierAxis.Rarity));
        var qualityFilled = Assert.Throws<InvalidDataException>(
            () => ItemTier.Parse(WithValue(FullQualityJson, "positiveAffixCount", "1"), "common.json", ItemTierAxis.Quality));

        Assert.Contains("positiveAffixCount", rarityMissing.Message);
        Assert.Contains("品质那一轴不填", qualityFilled.Message);
    }

    /// <summary>显示色不是色板里的一档，载入被拒，点名是哪一轴的哪一档。</summary>
    [Fact]
    public void 显示色不在色板里就被拒()
    {
        var catalog = Catalog(ShippedLikeTables(
            ("item_rarities/low.json", WithValue(FullRarityJson, "displayColor", "\"copper 9\"")),
            ("item_qualities/common.json", WithValue(FullQualityJson, "displayColor", "\"sky 5\""))));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains("稀有度「low」的显示色「copper 9」不在色板里", error.Message);
        Assert.Contains("品质「common」的显示色「sky 5」不在色板里", error.Message);
    }

    /// <summary>品质顶上那一级的两个值落在同一条色带上，载入被拒 —— 它们只靠星色分。</summary>
    [Fact]
    public void 品质顶上那一级同一条色带就被拒()
    {
        var catalog = Catalog(ShippedLikeTables(("item_qualities/mutated.json", QualityJson("mutated", 5, "ember 4"))));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains("色带「ember」", error.Message);
        Assert.Contains("legendary", error.Message);
        Assert.Contains("mutated", error.Message);
    }

    /// <summary>其余各档与顶上那一级同一条色带，同样被拒。</summary>
    [Fact]
    public void 其余各档与顶上那一级同色带也被拒()
    {
        var catalog = Catalog(ShippedLikeTables(("item_qualities/common.json", QualityJson("common", 2, "spirit 3"))));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains("品质「common」的显示色与顶上那一级的「mutated」同在色带「spirit」上", error.Message);
    }

    /// <summary>反证：顶上那一级各占一条色带、其余各档共用一条别的，读得进来。缺了它就分不出判的是撞色带还是凡是同色都拒。</summary>
    [Fact]
    public void 各占一条色带的品质表读得进来()
    {
        var items = ItemCatalog.Load(Catalog(ShippedLikeTables()), TestPalette.BandOf);

        Assert.Equal(["poor", "common", "fine", "superb", "legendary", "mutated"], items.Qualities.Select(q => q.Id));
    }

    /// <summary>排序位允许重复：同一级的两个值都在册，按排序位排、同一级里按标识排。</summary>
    [Fact]
    public void 排序位允许重复()
    {
        var items = ItemCatalog.Load(Catalog(ShippedLikeTables()), TestPalette.BandOf);

        var top = items.Qualities.Where(q => q.SortOrder == 5).Select(q => q.Id).ToList();
        Assert.Equal(["legendary", "mutated"], top);
        Assert.Equal(top, items.Qualities.TakeLast(2).Select(q => q.Id));
    }

    /// <summary>同一级的几档共用一个基础价系数：同一级里一个给了值、一个没给，载入被拒。</summary>
    [Fact]
    public void 同一级的系数不一样就被拒()
    {
        var catalog = Catalog(ShippedLikeTables(
            ("item_qualities/legendary.json", WithValue(QualityJson("legendary", 5, "ember 5"), "basePriceFactor", "2.5"))));

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(catalog, TestPalette.BandOf));

        Assert.Contains("排序位 5 那一级", error.Message);
    }

    /// <summary>
    /// 多放一份品质档位文件、不改代码，载入成功，而且它照自己的排序位排进去
    /// （设计仓 decisions/ADR-0025-取值集的家由加一个的代价决定.md 的内容型反证）。
    /// </summary>
    /// <remarks>读工程里那几份档位，再多放一份；代码里没有哪一处写着「品质有几档」或按序号取某一档。</remarks>
    [Fact]
    public void 多放一份品质档位文件不改代码就读得进来()
    {
        var files = ShippedTableFiles();
        files["item_qualities/heirloom.json"] = QualityJson("heirloom", 3, "ash 6");

        var items = ItemCatalog.Load(Catalog(files), PaletteFile.ReadPalette().BandOf);

        Assert.Equal(["poor", "common", "fine", "heirloom", "superb", "legendary", "mutated"], items.Qualities.Select(q => q.Id));
    }

    /// <summary>一条轴一档都没读到就被拒：目录名拼错或者没进发行包，不会有哪一份文件替它报错。</summary>
    [Fact]
    public void 一条轴一档都没有就被拒()
    {
        var files = ShippedLikeTables();
        foreach (var path in files.Keys.Where(p => p.StartsWith("item_qualities/", StringComparison.Ordinal)).ToList())
        {
            files.Remove(path);
        }

        var error = Assert.Throws<InvalidDataException>(() => ItemCatalog.Load(Catalog(files), TestPalette.BandOf));

        Assert.Contains("品质那一轴一档都没有", error.Message);
        Assert.Contains("item_qualities/", error.Message);
    }

    /// <summary>稀有度那一轴同一条反证：多放一份档位文件、不改代码，载入成功，排在它的排序位上。</summary>
    [Fact]
    public void 多放一份稀有度档位文件不改代码就读得进来()
    {
        var files = ShippedTableFiles();
        files["item_rarities/relic.json"] = RarityJson("relic", 4, "spirit 3");

        var items = ItemCatalog.Load(Catalog(files), PaletteFile.ReadPalette().BandOf);

        Assert.Equal(["low", "mid", "high", "relic"], items.Rarities.Select(r => r.Id));
    }

    // ── 工程里那一份 ──────────────────────────────────────────────────

    /// <summary>工程里那几份类别与档位，按仓库里的色板读得进来；它们要的文字文本表里都有。</summary>
    [Fact]
    public void 工程里的物品数据读得进来而且文字都在()
    {
        var items = ItemCatalog.Load(Catalog(ShippedTableFiles()), PaletteFile.ReadPalette().BandOf);
        var textPath = Path.Combine(RepoFiles.Root(), "data", "text", "zh-CN.json");
        var text = new TextCatalog(ContentJson.Parse<Dictionary<string, string>>(File.ReadAllText(textPath), textPath));

        Assert.NotEmpty(items.Rarities);
        Assert.NotEmpty(items.Qualities);
        Assert.Empty(text.MissingKeys(items.TextKeys));
    }

    // ── 工具 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 一份齐全的小表：三个类别、三档稀有度、六档品质（顶上那一级两个值各占一条色带），加一种物品。
    /// 传进来的那几份按路径顶掉或补上。
    /// </summary>
    private static Dictionary<string, string> ShippedLikeTables(params (string Path, string Json)[] overrides)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["item_categories/material.json"] = CategoryJson("material", "null"),
            ["item_categories/herb.json"] = CategoryJson("herb", "\"material\""),
            ["item_categories/seed.json"] = CategoryJson("seed", "null"),
            ["item_rarities/low.json"] = RarityJson("low", 1, "copper 3"),
            ["item_rarities/mid.json"] = RarityJson("mid", 2, "ice 4"),
            ["item_rarities/high.json"] = RarityJson("high", 3, "gold 5"),
            ["item_qualities/poor.json"] = QualityJson("poor", 1, "ash 6"),
            ["item_qualities/common.json"] = QualityJson("common", 2, "ash 6"),
            ["item_qualities/fine.json"] = QualityJson("fine", 3, "ash 6"),
            ["item_qualities/superb.json"] = QualityJson("superb", 4, "ash 6"),
            ["item_qualities/legendary.json"] = QualityJson("legendary", 5, "ember 5"),
            ["item_qualities/mutated.json"] = QualityJson("mutated", 5, "spirit 3"),
            ["items/turnip_seed.json"] = FullItemJson,
        };
        foreach (var (path, json) in overrides)
        {
            files[path] = json;
        }

        return files;
    }

    /// <summary>工程里那几个内容目录（类别、两条轴、物品）的全部文件，路径写成内容目录里的相对路径。</summary>
    private static Dictionary<string, string> ShippedTableFiles()
    {
        string[] directories =
        [
            ItemCategory.ContentDirectory,
            ItemTier.ContentDirectoryOf(ItemTierAxis.Rarity),
            ItemTier.ContentDirectoryOf(ItemTierAxis.Quality),
            ItemDefinition.ContentDirectory,
        ];
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var onDisk = Path.Combine(RepoFiles.Root(), "data", directory);
            if (!Directory.Exists(onDisk))
            {
                continue;
            }

            foreach (var path in Directory.GetFiles(onDisk, "*.json").Order(StringComparer.Ordinal))
            {
                files[$"{directory}/{Path.GetFileName(path)}"] = File.ReadAllText(path);
            }
        }

        return files;
    }

    private static string CategoryJson(string id, string parentJson) =>
        $$"""{ "id": "{{id}}", "nameKey": "item_category.{{id}}.name", "parentId": {{parentJson}} }""";

    private static string RarityJson(string id, int sortOrder, string color) =>
        $$"""
        { "id": "{{id}}", "nameKey": "item_rarity.{{id}}.name", "sortOrder": {{sortOrder}},
          "basePriceFactor": null, "positiveAffixCount": {{sortOrder - 1}}, "displayColor": "{{color}}" }
        """;

    private static string QualityJson(string id, int sortOrder, string color) =>
        $$"""
        { "id": "{{id}}", "nameKey": "item_quality.{{id}}.name", "sortOrder": {{sortOrder}},
          "basePriceFactor": null, "displayColor": "{{color}}" }
        """;

    private static ContentCatalog Catalog(Dictionary<string, string> files)
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", files));
        return catalog;
    }

    /// <summary>从一份 JSON 里去掉一个顶层键，用来造「缺字段」的输入。</summary>
    private static string WithoutKey(string json, string key) =>
        Rewrite(json, key, replacement: null);

    /// <summary>把一份 JSON 里一个顶层键的值换成给定的那一段 JSON；没有这个键就补上。</summary>
    private static string WithValue(string json, string key, string valueJson) =>
        Rewrite(json, key, valueJson);

    private static string Rewrite(string json, string key, string? replacement)
    {
        using var document = JsonDocument.Parse(json);
        var kept = document.RootElement
            .EnumerateObject()
            .Where(property => property.Name != key)
            .Select(property => $"\"{property.Name}\":{property.Value.GetRawText()}")
            .ToList();
        if (replacement is not null)
        {
            kept.Add($"\"{key}\":{replacement}");
        }

        return $"{{{string.Join(",", kept)}}}";
    }
}
