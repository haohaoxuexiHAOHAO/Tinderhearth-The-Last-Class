using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Progression;

/// <summary>在册的全部物品、类别与两条轴的档位，读进来时已经互相核过一遍。</summary>
/// <remarks>
/// 四样各是一个内容目录、一份文件一条，读法与作物定义同一套（<see cref="ContentCatalog.ResolveById"/>：基础内容加 mod 覆盖，
/// 同一个标识被两份文件用了当场报错，一份读不出来点名是哪一份、来自哪里）。每一份自己读得出来之后，跨文件的那几条
/// 在这里一次核完、一次全报，一行一处：物品指的类别与稀有度在不在册、类别的层级接不接得上、档位的显示色在不在色板里、
/// 品质顶上那一级的色带撞不撞、同一级的几档系数是不是同一个。
///
/// 档位按排序位排、排序位相同再按标识排，读的人遍历这两张表，不按序号取某一档（设计仓 design/物品系统.md 的
/// 「两条轴的档位各是一张在册的声明表」一节）—— 所以多放一份档位文件、不改代码，它就排进该在的位置。
///
/// 色板不由这一层读：规则层的这一侧不认识色板的格式，只问一句「这一项在色板里吗、属于哪条色带」，由调用方按
/// 那份色板答（引擎层读发行包里那一份，测试读仓库里那一份）。
/// </remarks>
public sealed class ItemCatalog
{
    private ItemCatalog(
        IReadOnlyDictionary<string, ItemDefinition> items,
        IReadOnlyDictionary<string, ItemCategory> categories,
        IReadOnlyList<ItemTier> rarities,
        IReadOnlyList<ItemTier> qualities)
    {
        Items = items;
        Categories = categories;
        Rarities = rarities;
        Qualities = qualities;
    }

    /// <summary>在册的物品，按标识查。</summary>
    public IReadOnlyDictionary<string, ItemDefinition> Items { get; }

    /// <summary>在册的类别，按标识查。</summary>
    public IReadOnlyDictionary<string, ItemCategory> Categories { get; }

    /// <summary>稀有度那一轴在册的几档，由低到高。</summary>
    public IReadOnlyList<ItemTier> Rarities { get; }

    /// <summary>品质那一轴在册的几档，由低到高；同一级的几档挨着、按标识排。</summary>
    public IReadOnlyList<ItemTier> Qualities { get; }

    /// <summary>这一批要在文本表里有的全部键：每种物品的名字与描述、每个类别与每一档的名字。</summary>
    public IReadOnlyList<string> TextKeys =>
        [
            .. Items.Values.OrderBy(i => i.Id, StringComparer.Ordinal).SelectMany(i => i.TextKeys),
            .. Categories.Values.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => c.NameKey),
            .. Rarities.Select(t => t.NameKey),
            .. Qualities.Select(t => t.NameKey),
        ];

    /// <summary>读内容目录里的物品、类别与两条轴的档位，并互相核一遍。</summary>
    /// <param name="catalog">内容目录。</param>
    /// <param name="paletteBandOf">色板里那一项属于哪条色带；不在色板里返回 <c>null</c>。</param>
    public static ItemCatalog Load(ContentCatalog catalog, Func<string, string?> paletteBandOf)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var categories = catalog.ResolveById(ItemCategory.ContentDirectory, e => ItemCategory.Parse(e.Text, e.RelativePath), c => c.Id);
        var rarities = LoadTiers(catalog, ItemTierAxis.Rarity);
        var qualities = LoadTiers(catalog, ItemTierAxis.Quality);
        var items = catalog.ResolveById(ItemDefinition.ContentDirectory, e => ItemDefinition.Parse(e.Text, e.RelativePath), i => i.Id);

        return Build(items.Values, categories.Values, rarities, qualities, paletteBandOf);
    }

    /// <summary>从已经各自读出来的四样建一份，跨文件的那几条在这里一次核完。</summary>
    /// <remarks>测试从这里进，不经内容目录；内容都从 <see cref="Load"/> 走，那一条最后也落到这里。</remarks>
    public static ItemCatalog Build(
        IEnumerable<ItemDefinition> items,
        IEnumerable<ItemCategory> categories,
        IEnumerable<ItemTier> rarities,
        IEnumerable<ItemTier> qualities,
        Func<string, string?> paletteBandOf)
    {
        ArgumentNullException.ThrowIfNull(paletteBandOf);

        var categoryTable = categories.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var itemTable = items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var rarityList = Ordered(rarities);
        var qualityList = Ordered(qualities);

        var problems = new List<string>();
        problems.AddRange(EmptyAxisProblems(ItemTierAxis.Rarity, rarityList));
        problems.AddRange(EmptyAxisProblems(ItemTierAxis.Quality, qualityList));
        problems.AddRange(CategoryProblems(categoryTable));
        problems.AddRange(ItemProblems(itemTable.Values, categoryTable, rarityList));
        problems.AddRange(ColorProblems(ItemTierAxis.Rarity, rarityList, paletteBandOf));
        problems.AddRange(ColorProblems(ItemTierAxis.Quality, qualityList, paletteBandOf));
        problems.AddRange(TopQualityBandProblems(qualityList, paletteBandOf));
        problems.AddRange(SameLevelFactorProblems(qualityList));

        return problems.Count == 0
            ? new ItemCatalog(itemTable, categoryTable, rarityList, qualityList)
            : throw new InvalidDataException(
                $"物品与档位互相对不上，共 {problems.Count} 处：{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    private static List<ItemTier> LoadTiers(ContentCatalog catalog, ItemTierAxis axis) =>
        [.. catalog.ResolveById(ItemTier.ContentDirectoryOf(axis), e => ItemTier.Parse(e.Text, e.RelativePath, axis), t => t.Id).Values];

    private static List<ItemTier> Ordered(IEnumerable<ItemTier> tiers) =>
        [.. tiers.OrderBy(t => t.SortOrder).ThenBy(t => t.Id, StringComparer.Ordinal)];

    /// <summary>一条轴一档都没有，报一句。</summary>
    /// <remarks>
    /// 目录名拼错、或者整个目录没进发行包，读出来就是空的，而一份文件都没读到不会有任何一份报错 —— 表现只是后面
    /// 用到那一轴的地方都查不到档。物品还没有时，「指不到在册的稀有度」那一条也兜不住它。
    /// </remarks>
    private static IEnumerable<string> EmptyAxisProblems(ItemTierAxis axis, IReadOnlyList<ItemTier> tiers)
    {
        if (tiers.Count == 0)
        {
            yield return $"{AxisName(axis)}那一轴一档都没有：内容目录 {ItemTier.ContentDirectoryOf(axis)}/ 下没有档位文件";
        }
    }

    /// <summary>父类别指不到的、层级绕回自己的，各报一句。</summary>
    private static IEnumerable<string> CategoryProblems(IReadOnlyDictionary<string, ItemCategory> categories)
    {
        foreach (var category in categories.Values.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (category.ParentId is string parent && !categories.ContainsKey(parent))
            {
                yield return $"类别「{category.Id}」的上一层「{parent}」不在册";
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal) { category.Id };
            for (var up = category.ParentId; up is not null && categories.TryGetValue(up, out var next); up = next.ParentId)
            {
                if (!seen.Add(up))
                {
                    yield return $"类别「{category.Id}」往上走绕回了「{up}」—— 层级不能成圈";
                    break;
                }
            }
        }
    }

    /// <summary>物品指的类别或稀有度不在册的，各报一句、点名是哪种物品的哪一格。</summary>
    private static IEnumerable<string> ItemProblems(
        IEnumerable<ItemDefinition> items,
        IReadOnlyDictionary<string, ItemCategory> categories,
        IReadOnlyList<ItemTier> rarities)
    {
        foreach (var item in items.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            if (!categories.ContainsKey(item.CategoryId))
            {
                yield return $"物品「{item.Id}」的类别「{item.CategoryId}」不在册";
            }

            if (!rarities.Any(r => r.Id == item.RarityId))
            {
                yield return $"物品「{item.Id}」的稀有度「{item.RarityId}」不在册";
            }
        }
    }

    /// <summary>显示色不在色板里的档，各报一句。</summary>
    private static IEnumerable<string> ColorProblems(
        ItemTierAxis axis, IReadOnlyList<ItemTier> tiers, Func<string, string?> bandOf) =>
        tiers
            .Where(t => bandOf(t.DisplayColor) is null)
            .Select(t => $"{AxisName(axis)}「{t.Id}」的显示色「{t.DisplayColor}」不在色板里");

    /// <summary>
    /// 品质排序位最高那一级的几个值彼此不许同一条色带，其余各档也不许与它们同一条色带。
    /// </summary>
    /// <remarks>
    /// 顶上那一级的几个值星数相同、只靠星色分（设计仓台账 <c>UI-40</c>）：撞了色带，它们就不再是标志。
    /// 不在色板里的那几档上一条已经报过，这里跳过，免得同一处报两句。
    /// </remarks>
    private static IEnumerable<string> TopQualityBandProblems(IReadOnlyList<ItemTier> qualities, Func<string, string?> bandOf)
    {
        if (qualities.Count == 0)
        {
            yield break;
        }

        var topLevel = qualities.Max(q => q.SortOrder);
        var top = qualities.Where(q => q.SortOrder == topLevel && bandOf(q.DisplayColor) is not null).ToList();
        foreach (var clash in top.GroupBy(q => bandOf(q.DisplayColor)!, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            yield return $"品质顶上那一级的「{string.Join("」「", clash.Select(q => q.Id))}」都在色带「{clash.Key}」上 —— "
                + "那一级的几个值只靠星色分，色带必须各不相同";
        }

        // 顶上那一级自己撞了色带的上面已经报过；这里每条色带只记第一个，免得建表时撞键。
        var topBands = top
            .GroupBy(q => bandOf(q.DisplayColor)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        foreach (var lower in qualities.Where(q => q.SortOrder != topLevel))
        {
            if (bandOf(lower.DisplayColor) is string band && topBands.TryGetValue(band, out var owner))
            {
                yield return $"品质「{lower.Id}」的显示色与顶上那一级的「{owner}」同在色带「{band}」上 —— "
                    + "其余各档不许与顶上那一级同一条色带";
            }
        }
    }

    /// <summary>品质同一级的几档共用一个基础价系数：同一级里系数不一样的，报一句。</summary>
    /// <remarks>级是算账用的、值是显示用的（传奇与变异同级、同系数、不同名），出处在设计仓 design/数值模型.md 的定价那一节。</remarks>
    private static IEnumerable<string> SameLevelFactorProblems(IReadOnlyList<ItemTier> qualities) =>
        qualities
            .GroupBy(q => q.SortOrder)
            .Where(level => level.Select(q => q.BasePriceFactor).Distinct().Count() > 1)
            .Select(level => $"品质排序位 {level.Key} 那一级的几档基础价系数不一样（"
                + string.Join("、", level.Select(q => $"{q.Id} 是 {q.BasePriceFactor?.ToString() ?? "null"}"))
                + "）—— 同一级共用一个系数");

    private static string AxisName(ItemTierAxis axis) => axis switch
    {
        ItemTierAxis.Rarity => "稀有度",
        ItemTierAxis.Quality => "品质",
        _ => axis.ToString(),
    };
}
