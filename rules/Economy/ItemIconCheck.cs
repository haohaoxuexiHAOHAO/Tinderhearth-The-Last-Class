using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>一份物品图标里配了什么：给哪一个物品、图标那一张配没配、变异那一张配没配、勾没勾同原图。</summary>
/// <remarks>由引擎层从图标资源读出来。规则层只看每一格配没配，看不见任何图片路径。</remarks>
/// <param name="ItemId">它配给哪一个物品。</param>
/// <param name="HasIcon">图标那一张配没配。</param>
/// <param name="HasMutatedIcon">变异那一张配没配。</param>
/// <param name="MutatedSameAsIcon">勾没勾「变异那一张同原图」。</param>
public sealed record ItemIconEntry(string ItemId, bool HasIcon, bool HasMutatedIcon, bool MutatedSameAsIcon);

/// <summary>物品图标：载入时那一次核对，以及画一堆东西时挑哪一张。</summary>
/// <remarks>
/// 每个物品一份图标资源，资源上写它是哪个物品，物品定义上没有这一格（设计仓 design/物品系统.md 字段表的「图标」与
/// 「变异时的图标」两行）。所以缺不缺、重不重只能从那一组图标资源里读出来，判在这里，与作物素材同一个做法
/// （<see cref="CropArtCheck"/>）。
///
/// 一次全报、一行一处：全部物品一起配的时候，一处一处改、一次一次重跑，作者要来回跑很多趟。在册却没有图标的物品
/// 也一种一行 —— 一份都还没配时，这几行就是要配的那张清单。
/// </remarks>
public static class ItemIconCheck
{
    /// <summary>这一堆画不画变异那一张：品质是变异的那一档才换，其余各档一律画图标那一张。</summary>
    /// <remarks>变异品质从哪来见 <see cref="Harvesting.MutatedQualityId"/>；无品质的东西没有品质，传 <c>null</c>。</remarks>
    public static bool ShowsMutated(string? qualityId) => qualityId == Harvesting.MutatedQualityId;

    /// <summary>核那一组图标资源。全对返回空表；顺序先按资源在那一组里的序号，最后是没有图标的那几种。</summary>
    /// <param name="items">在册的全部物品。</param>
    /// <param name="icons">那一组图标资源，顺序与检查器里一致；空着的那一项是 <c>null</c>。</param>
    public static IReadOnlyList<string> Problems(ItemCatalog items, IReadOnlyList<ItemIconEntry?> icons)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(icons);

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

            problems.AddRange(ProblemsOf(icon, item));
        }

        problems.AddRange(items.Items.Keys
            .Where(id => !firstIndexOf.ContainsKey(id))
            .Order(StringComparer.Ordinal)
            .Select(id => $"物品「{id}」在册但没有图标 —— 在 Item Icons 里给它配一份"));

        return problems;
    }

    private static IEnumerable<string> ProblemsOf(ItemIconEntry icon, ItemDefinition item)
    {
        if (!icon.HasIcon)
        {
            yield return $"物品「{item.Id}」的图标（Icon）是空的";
        }

        if (!item.HasQuality)
        {
            if (icon.HasMutatedIcon || icon.MutatedSameAsIcon)
            {
                yield return $"物品「{item.Id}」声明了无品质，变异那一格（Mutated Icon 与 Mutated Same As Icon）用不上 —— "
                    + "清掉它，不然看着像是它会变异";
            }

            yield break;
        }

        if (!icon.HasMutatedIcon && !icon.MutatedSameAsIcon)
        {
            yield return $"物品「{item.Id}」有品质，缺变异那一张（Mutated Icon）—— 不另画就勾上 Mutated Same As Icon";
        }
        else if (icon.HasMutatedIcon && icon.MutatedSameAsIcon)
        {
            yield return $"物品「{item.Id}」配了变异那一张（Mutated Icon），又勾了 Mutated Same As Icon —— 两样只留一样";
        }
    }
}
