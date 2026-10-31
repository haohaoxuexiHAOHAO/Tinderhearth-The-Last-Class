using System.Globalization;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>背包页上的一格画什么。</summary>
/// <param name="ItemId">哪一样：图标按它取。</param>
/// <param name="QualityId">哪一档品质，无品质的是 <c>null</c>：图标按它挑（变异那一档画变异那一张）。</param>
/// <param name="Count">几件。</param>
/// <param name="RarityColor">稀有度框染哪一色：那一样的稀有度那一档声明的显示色，是色板里一项的名字。</param>
/// <param name="QualityColor">品质点染哪一色：那一档品质声明的显示色；无品质的是 <c>null</c>，不画品质点。</param>
public sealed record BagCell(string ItemId, string? QualityId, int Count, string RarityColor, string? QualityColor)
{
    /// <summary>角上写不写件数：只有一件不写，一整页的「1」不带任何信息，却占掉图标本来就不多的地方。</summary>
    public bool ShowsCount => Count > 1;
}

/// <summary>手环里的背包那一页：每一格画什么、选中那一格下面那一行写什么、还剩几个空格。</summary>
/// <remarks>
/// 这一页答「我要的那样东西在哪，我还背得下吗」（设计仓 design/界面系统.md 的「手环上：背包」一节）：种类与品质两维都
/// 看得见，所以每一格画图标、品质点与稀有度框，两条轴用两种手段。颜色不在这里定，读的是那一档自己声明的显示色
/// （设计仓 design/物品系统.md 的「两条轴的档位各是一张在册的声明表」一节）。字全在文本表里，这里只挑哪一句、填哪几个值。
///
/// 这一页没有自己的状态：显示的全是背包的当前值，每次打开现读一遍。
/// </remarks>
public static class BagPage
{
    /// <summary>还剩几个空格那一行，填空格数。</summary>
    public const string FreeSlotsKey = "ui.bag.free_slots";

    /// <summary>选中那一格那一行，依次填名称、品质名与件数。</summary>
    public const string InfoKey = "ui.bag.info";

    /// <summary>选中那一格是无品质的东西时那一行，依次填名称与件数。</summary>
    public const string InfoNoQualityKey = "ui.bag.info_no_quality";

    /// <summary>这一页要在文本表里有的那几条。</summary>
    public static IReadOnlyList<string> TextKeys => [FreeSlotsKey, InfoKey, InfoNoQualityKey];

    /// <summary>每一格画什么，按格号排；空格是 <c>null</c>，只画格子。</summary>
    public static IReadOnlyList<BagCell?> CellsOf(Bag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);

        return [.. bag.Slots.Select(slot => slot is { } stack ? CellOf(stack, bag.Items) : null)];
    }

    /// <summary>还剩几个空格那一行。</summary>
    public static string FreeSlotsLine(Bag bag, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(bag);
        return Format(text, FreeSlotsKey, bag.EmptySlotCount);
    }

    /// <summary>选中那一格下面那一行：名称、品质与件数；无品质的东西不写品质。</summary>
    public static string InfoLine(BagCell cell, ItemCatalog items, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(text);

        var name = text[items.Items[cell.ItemId].NameKey];
        return cell.QualityId is { } quality
            ? Format(text, InfoKey, name, text[TierOf(items.Qualities, quality).NameKey], cell.Count)
            : Format(text, InfoNoQualityKey, name, cell.Count);
    }

    private static BagCell CellOf(ItemStack stack, ItemCatalog items)
    {
        var item = items.Items[stack.ItemId];
        return new BagCell(
            stack.ItemId,
            stack.QualityId,
            stack.Count,
            TierOf(items.Rarities, item.RarityId).DisplayColor,
            stack.QualityId is { } quality ? TierOf(items.Qualities, quality).DisplayColor : null);
    }

    /// <summary>按标识取一档。背包里的每一堆都核过在册，取不到就是背包与物品表不是同一份。</summary>
    private static ItemTier TierOf(IReadOnlyList<ItemTier> tiers, string id) =>
        tiers.FirstOrDefault(t => t.Id == id)
            ?? throw new InvalidOperationException($"档位「{id}」不在册 —— 背包里的东西应当在放进去时就核过");

    /// <summary>按文本表里那一条的写法填值。那一条的占位写错了是内容的错，报出是哪一条。</summary>
    private static string Format(TextCatalog text, string key, params object[] values)
    {
        ArgumentNullException.ThrowIfNull(text);

        var template = text[key];
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, values);
        }
        catch (FormatException e)
        {
            throw new InvalidDataException(
                $"文本表里「{key}」那一条的占位写错了（「{template}」，要填 {values.Length} 个）：{e.Message}", e);
        }
    }
}
