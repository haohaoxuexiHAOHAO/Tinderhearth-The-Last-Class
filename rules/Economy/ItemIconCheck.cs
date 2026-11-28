using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>一张图标的像素尺寸。</summary>
/// <remarks>不用界面那一侧的像素尺寸：规则层的经营这一侧不依赖界面那一侧。</remarks>
public readonly record struct IconSize(int Width, int Height)
{
    /// <summary>写成「宽×高」。</summary>
    public override string ToString() => $"{Width}×{Height}";
}

/// <summary>一份物品图标里配了什么：给哪一个物品、图标与变异那一张各多大、勾没勾同原图。</summary>
/// <remarks>由引擎层从图标资源读出来。规则层只看每一格配没配、配的那一张多大，看不见任何图片路径。</remarks>
/// <param name="ItemId">它配给哪一个物品。</param>
/// <param name="Icon">图标那一张的像素尺寸；没配是 <c>null</c>。</param>
/// <param name="MutatedIcon">变异那一张的像素尺寸；没配是 <c>null</c>。</param>
/// <param name="MutatedSameAsIcon">勾没勾「变异那一张同原图」。</param>
/// <param name="NotDrawnYet">勾没勾「暂无」：这一样东西的图还没画。勾了的那一份，图标两格都要空着。</param>
public sealed record ItemIconEntry(
    string ItemId, IconSize? Icon, IconSize? MutatedIcon, bool MutatedSameAsIcon, bool NotDrawnYet);

/// <summary>物品图标：载入时那一次核对，以及画一堆东西时挑哪一张、图标应当多大。</summary>
/// <remarks>
/// 每个物品一份图标资源，资源上写它是哪个物品，物品定义上没有这一格（设计仓 design/物品系统.md 字段表的「图标」与
/// 「变异时的图标」两行）。所以缺不缺、重不重、多大只能从那一组图标资源里读出来，判在这里，与作物素材同一个做法
/// （<see cref="CropArtCheck"/>）。
///
/// 一次全报、一行一处：全部物品一起配的时候，一处一处改、一次一次重跑，作者要来回跑很多趟。在册却没有图标的物品
/// 也一种一行 —— 一份都还没配时，这几行就是要配的那张清单。
///
/// 图标的宽高必须正好是占地乘基础单位：背包页把图原样画在那一堆占的那一块上、不缩放，大小对不上就画不满那一块或者
/// 压出去，而那不报错。一格多少像素由调用方给（引擎层给的是界面那一侧的基础单位），与 <see cref="SeedBatches"/>
/// 取随身栏格数同一个做法：经营这一侧不依赖界面那一侧。
///
/// 图还没画的那一样勾「暂无」，游戏照常起来，格子里写那一样名字的第一个字（口径在设计仓 production/像素绘制原则.md
/// 的「像素基准」一节）。「暂无」要显式勾，不拿空着的那一格当暂无：空着与漏填长得一样，所以什么都没写照旧报。
/// 勾了暂无又配了图也报：图画好了却忘了去掉暂无，格子里照旧写字、那张图白配，而那不报错。
/// </remarks>
public static class ItemIconCheck
{
    /// <summary>这一堆画不画变异那一张：品质是变异的那一档才换，其余各档一律画图标那一张。</summary>
    /// <remarks>变异品质从哪来见 <see cref="Harvesting.MutatedQualityId"/>；无品质的东西没有品质，传 <c>null</c>。</remarks>
    public static bool ShowsMutated(string? qualityId) => qualityId == Harvesting.MutatedQualityId;

    /// <summary>一件东西的图标应当多大：占地的宽高各乘一格的像素数。</summary>
    /// <param name="footprint">它的占地。</param>
    /// <param name="cellPixels">背包网格一格多少像素，至少 1。基础单位出自设计仓 production/像素绘制原则.md 的「像素基准」一节。</param>
    public static IconSize IconSizeFor(ItemFootprint footprint, int cellPixels)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellPixels, 1);
        return new IconSize(Width: footprint.Width * cellPixels, Height: footprint.Height * cellPixels);
    }

    /// <summary>核那一组图标资源。全对返回空表；顺序先按资源在那一组里的序号，最后是没有图标的那几种。</summary>
    /// <param name="items">在册的全部物品。</param>
    /// <param name="icons">那一组图标资源，顺序与检查器里一致；空着的那一项是 <c>null</c>。</param>
    /// <param name="cellPixels">背包网格一格多少像素，见 <see cref="IconSizeFor"/>。</param>
    public static IReadOnlyList<string> Problems(ItemCatalog items, IReadOnlyList<ItemIconEntry?> icons, int cellPixels)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellPixels, 1);

        var problems = new List<string>();
        var firstIndexOf = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < icons.Count; index++)
        {
            if (icons[index] is not { } icon)
            {
                problems.Add($"Item Icons 第 {index} 项（从 0 数）是空的");
                continue;
            }

            if (!firstIndexOf.TryAdd(icon.ItemId, index))
            {
                problems.Add($"Item Icons 第 {firstIndexOf[icon.ItemId]} 项与第 {index} 项（从 0 数）都配给了物品「{icon.ItemId}」"
                    + " —— 复制出来的那一份多半没改 Item Id");
            }

            if (!items.Items.TryGetValue(icon.ItemId, out var item))
            {
                problems.Add($"图标配的物品「{icon.ItemId}」不在册 —— Item Id 要与物品定义里的 id 逐字符一致");
                continue;
            }

            if (icon.NotDrawnYet)
            {
                if (NotDrawnYetProblem(icon) is string conflict)
                {
                    problems.Add(conflict);
                }

                continue;
            }

            problems.AddRange(ProblemsOf(icon, item, cellPixels));
        }

        problems.AddRange(items.Items.Keys
            .Where(id => !firstIndexOf.ContainsKey(id))
            .Order(StringComparer.Ordinal)
            .Select(id => $"物品「{id}」在册但没有图标 —— 在 Item Icons 里给它配一份"));

        return problems;
    }

    /// <summary>那一组里还是「暂无」的那几样，按标识排、不重复。启动日志照它列出还剩哪几样没画。</summary>
    /// <param name="icons">那一组图标资源；空着的那一项是 <c>null</c>，不算。</param>
    public static IReadOnlyList<string> NotDrawnYet(IReadOnlyList<ItemIconEntry?> icons)
    {
        ArgumentNullException.ThrowIfNull(icons);
        return [.. icons.OfType<ItemIconEntry>()
            .Where(icon => icon.NotDrawnYet)
            .Select(icon => icon.ItemId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>勾了「暂无」，却又配了图或勾了同原图：一件报一行，写清两条出路。</summary>
    private static string? NotDrawnYetProblem(ItemIconEntry icon) =>
        icon.Icon is not null || icon.MutatedIcon is not null || icon.MutatedSameAsIcon
            ? $"物品「{icon.ItemId}」勾了 Not Drawn Yet（暂无），又配了图或勾了 Mutated Same As Icon —— "
                + "图画好了就去掉暂无，还没画就把那几格清空"
            : null;

    private static IEnumerable<string> ProblemsOf(ItemIconEntry icon, ItemDefinition item, int cellPixels)
    {
        if (icon.Icon is null)
        {
            yield return $"物品「{item.Id}」的图标（Icon）是空的";
        }

        if (MutatedProblem(icon, item) is string mutated)
        {
            yield return mutated;
        }

        if (SizeProblem(icon, item, cellPixels) is string size)
        {
            yield return size;
        }
    }

    private static string? MutatedProblem(ItemIconEntry icon, ItemDefinition item)
    {
        var hasMutated = icon.MutatedIcon is not null;
        if (!item.HasQuality)
        {
            return hasMutated || icon.MutatedSameAsIcon
                ? $"物品「{item.Id}」声明了无品质，变异那一格（Mutated Icon 与 Mutated Same As Icon）用不上 —— "
                    + "清掉它，不然看着像是它会变异"
                : null;
        }

        return (hasMutated, icon.MutatedSameAsIcon) switch
        {
            (false, false) => $"物品「{item.Id}」有品质，缺变异那一张（Mutated Icon）—— 不另画就勾上 Mutated Same As Icon",
            (true, true) => $"物品「{item.Id}」配了变异那一张（Mutated Icon），又勾了 Mutated Same As Icon —— 两样只留一样",
            _ => null,
        };
    }

    /// <summary>图标或变异那一张的宽高不等于占地乘一格的像素数时，一件物品报一行，写清应当多大、实际多大。</summary>
    /// <remarks>没配的那一张上面已经报过缺；无品质的东西不画变异那一张，配了也只报用不上，这里都不再核尺寸。</remarks>
    private static string? SizeProblem(ItemIconEntry icon, ItemDefinition item, int cellPixels)
    {
        var expected = IconSizeFor(item.Footprint, cellPixels);
        var wrong = new List<string>();
        if (icon.Icon is { } drawn && drawn != expected)
        {
            wrong.Add($"Icon 是 {drawn}");
        }

        if (item.HasQuality && icon.MutatedIcon is { } mutated && mutated != expected)
        {
            wrong.Add($"Mutated Icon 是 {mutated}");
        }

        return wrong.Count == 0
            ? null
            : $"物品「{item.Id}」占地 {item.Footprint} 格，图标应当正好 {expected}：{string.Join("，", wrong)}";
    }
}
