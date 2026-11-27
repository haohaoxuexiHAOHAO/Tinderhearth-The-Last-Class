using System.Globalization;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;

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

/// <summary>背包页上一堆占的那一块。</summary>
/// <param name="Index">那一块左上角那一格在页上的号（与 <see cref="BagControls.PlaceOf"/> 同一个号）。</param>
/// <param name="Column">左上角那一格在页上第几列，从 0 数。</param>
/// <param name="Row">左上角那一格在页上第几行，从 0 数；随身栏那几行算在里面。</param>
/// <param name="Width">宽几格：那一样的占地。</param>
/// <param name="Height">高几格：那一样的占地。</param>
/// <param name="Content">画什么。</param>
public sealed record BagBlock(int Index, int Column, int Row, int Width, int Height, BagCell Content);

/// <summary>拿着一堆指着别处时，指着的地方画什么：要占的那一块在页上的位置与大小，放不放得下。</summary>
/// <param name="Column">那一块左上角在页上第几列，从 0 数。</param>
/// <param name="Row">那一块左上角在页上第几行，从 0 数。</param>
/// <param name="Width">宽几格。出了网格的那一截不算。</param>
/// <param name="Height">高几格。出了网格的那一截不算。</param>
/// <param name="Fits">按下去会挪过去或合进去。</param>
public sealed record BagDropPreview(int Column, int Row, int Width, int Height, bool Fits);

/// <summary>游戏菜单里的背包那一页：每一格画什么、选中那一格下面那一行写什么、还剩几个空格。</summary>
/// <remarks>
/// 这一页答「我要的那样东西在哪，我还背得下吗」（设计仓 design/界面系统.md 的「游戏菜单上：背包」一节）：种类与品质两维都
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

    /// <summary>筛选那一行打头的那个词。</summary>
    public const string FilterKey = "ui.bag.filter";

    /// <summary>按类别筛那个按钮在不筛类别时写什么。</summary>
    public const string AnyCategoryKey = "ui.bag.filter.any_category";

    /// <summary>按品质筛那个按钮在不筛品质时写什么。</summary>
    public const string AnyQualityKey = "ui.bag.filter.any_quality";

    /// <summary>一键整理那个按钮。</summary>
    public const string TidyKey = "ui.bag.tidy";

    /// <summary>丢弃那个按钮。</summary>
    public const string DiscardKey = "ui.bag.discard";

    /// <summary>丢之前那一问，依次填名称、品质名与件数。</summary>
    public const string DiscardAskKey = "ui.bag.discard_ask";

    /// <summary>丢之前那一问，无品质的东西，依次填名称与件数。</summary>
    public const string DiscardAskNoQualityKey = "ui.bag.discard_ask_no_quality";

    /// <summary>那一问上确认丢的按钮。</summary>
    public const string DiscardYesKey = "ui.bag.discard_yes";

    /// <summary>那一问上不丢的按钮。</summary>
    public const string DiscardNoKey = "ui.bag.discard_no";

    /// <summary>丢不掉时屏上那一行（即时提示），填名称。</summary>
    public const string DiscardRejectedKey = "ui.bag.discard_rejected";

    /// <summary>占不止一格的东西要挪进随身栏时屏上那一行（即时提示）。</summary>
    public const string TooBigForCarryKey = "ui.bag.too_big_for_carry";

    /// <summary>排序或一键整理照新的先后摆不下全部、一格没动时屏上那一行（即时提示）。</summary>
    public const string NoRoomToArrangeKey = "ui.bag.no_room_to_arrange";

    /// <summary>三个排序键各自那个按钮上的字。</summary>
    public static IReadOnlyDictionary<BagSortKey, string> SortKeyKeys { get; } = new Dictionary<BagSortKey, string>
    {
        [BagSortKey.Quality] = "ui.bag.sort.quality",
        [BagSortKey.Category] = "ui.bag.sort.category",
        [BagSortKey.Recent] = "ui.bag.sort.recent",
    };

    /// <summary>这一页要在文本表里有的那几条。</summary>
    public static IReadOnlyList<string> TextKeys =>
    [
        FreeSlotsKey, InfoKey, InfoNoQualityKey, FilterKey, AnyCategoryKey, AnyQualityKey, TidyKey, DiscardKey,
        DiscardAskKey, DiscardAskNoQualityKey, DiscardYesKey, DiscardNoKey, DiscardRejectedKey, TooBigForCarryKey,
        NoRoomToArrangeKey, .. SortKeyKeys.Values,
    ];

    /// <summary>按类别筛那个按钮上写什么：现在筛的那一类的名字，不筛时写「全部类别」。</summary>
    public static string CategoryChoiceLabel(BagFilter filter, ItemCatalog items, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(text);
        return filter.CategoryId is { } category ? text[items.Categories[category].NameKey] : text[AnyCategoryKey];
    }

    /// <summary>按品质筛那个按钮上写什么：现在筛的那一档的名字，不筛时写「全部品质」。</summary>
    public static string QualityChoiceLabel(BagFilter filter, ItemCatalog items, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(text);
        return filter.QualityId is { } quality ? text[TierOf(items.Qualities, quality).NameKey] : text[AnyQualityKey];
    }

    /// <summary>丢之前那一问：写明丢的是哪一样、什么品质、几件；无品质的东西不写品质。</summary>
    public static string DiscardAsk(BagCell cell, ItemCatalog items, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(text);

        var name = text[items.Items[cell.ItemId].NameKey];
        return cell.QualityId is { } quality
            ? Format(text, DiscardAskKey, name, text[TierOf(items.Qualities, quality).NameKey], cell.Count)
            : Format(text, DiscardAskNoQualityKey, name, cell.Count);
    }

    /// <summary>那一样丢不掉时屏上那一行。丢之前先问它，丢不掉的就不弹那一问。</summary>
    /// <returns>丢得掉时是 <c>null</c>。</returns>
    public static string? DiscardRejection(BagCell cell, ItemCatalog items, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(text);

        var item = items.Items[cell.ItemId];
        return item.Discardable ? null : Format(text, DiscardRejectedKey, text[item.NameKey]);
    }

    /// <summary>每一格画什么：先随身栏那一排，再网格按格号排（与 <see cref="BagControls.PlaceOf"/> 同一个号）；空格是 <c>null</c>，只画格子。</summary>
    public static IReadOnlyList<BagCell?> CellsOf(Bag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);

        return [.. bag.Carry.Concat(bag.Slots).Select(slot => slot is { } stack ? CellOf(stack, bag.Items) : null)];
    }

    /// <summary>页上第 <paramref name="index"/> 格在第几列、第几行：随身栏那几格占开头那几行，网格从下一行开始。</summary>
    /// <remarks>行数怎么算见 <see cref="BagPageLayout.CarryRows"/>。</remarks>
    public static (int Column, int Row) PositionOf(Bag bag, int index)
    {
        var place = BagControls.PlaceOf(bag, index);
        var columns = bag.GridWidth;
        return place.InCarry
            ? (place.Index % columns, place.Index / columns)
            : (place.Index % columns, BagPageLayout.CarryRows(bag.CarryCount, columns) + (place.Index / columns));
    }

    /// <summary>页上第 <paramref name="index"/> 格被哪一堆盖着：那一堆左上角那一格在页上的号；空着是 <c>null</c>。</summary>
    /// <remarks>点到一块里的任何一格都是那一堆，所以拿起、下面那一行与丢弃都先经这里找到它。</remarks>
    public static int? AnchorIndexOf(Bag bag, int index) =>
        bag.AnchorOf(BagControls.PlaceOf(bag, index)) is not { } at ? null : IndexOf(bag, at);

    /// <summary>每一堆占的那一块：随身栏那一排里的各占一格，网格里的照占地占一块。先随身栏、再网格按格号排。</summary>
    /// <remarks>看不看得见（当前筛选）不在这里判，在 <see cref="BagControls.Shows"/>。</remarks>
    public static IReadOnlyList<BagBlock> BlocksOf(Bag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);

        var blocks = new List<BagBlock>();
        for (var i = 0; i < bag.CarryCount + bag.SlotCount; i++)
        {
            var place = BagControls.PlaceOf(bag, i);
            if (bag.StackAt(place) is not { } stack)
            {
                continue;
            }

            var footprint = bag.Items.Items[stack.ItemId].Footprint;
            var (column, row) = PositionOf(bag, i);
            blocks.Add(new BagBlock(i, column, row, footprint.Width, footprint.Height, CellOf(stack, bag.Items)));
        }

        return blocks;
    }

    /// <summary>
    /// 拿着页上第 <paramref name="grabbed"/> 格那一堆、指着第 <paramref name="pointed"/> 格时，指着的地方画什么；
    /// 指着的就是拿起时点的那一格（点下去是放回去）时是 <c>null</c>，什么都不画。
    /// </summary>
    /// <remarks>
    /// 要占的那一块照挪的规则算：拿起时点的那一格对准指着的那一格（<see cref="Bag.Move"/>）；指着另一堆同标识的东西是合进去，
    /// 那一块就是那一堆；指着随身栏那一排，那一块就是那一格。出了网格的那一截不画，只画网格里那一部分。
    /// 放不放得下问 <see cref="Bag.PreviewMove"/>，与按下去时是同一套判法。被筛掉的格画不画不在这里判，在
    /// <see cref="BagControls.Preview"/>。
    /// </remarks>
    public static BagDropPreview? DropPreviewOf(Bag bag, int grabbed, int pointed)
    {
        ArgumentNullException.ThrowIfNull(bag);
        if (grabbed == pointed)
        {
            return null;
        }

        var from = BagControls.PlaceOf(bag, grabbed);
        var to = BagControls.PlaceOf(bag, pointed);
        var anchor = bag.AnchorOf(from)
            ?? throw new ArgumentException($"背包{from}空着，手上不可能拿着东西", nameof(grabbed));
        var moving = bag.StackAt(anchor)!.Value;
        var fits = bag.PreviewMove(from, to) is BagMoveResult.Moved or BagMoveResult.Merged;

        if (bag.AnchorOf(to) is { } at && at != anchor && bag.StackAt(at) is { } other
            && other.Holds(moving.ItemId, moving.QualityId))
        {
            var target = BlocksOf(bag).Single(b => b.Index == IndexOf(bag, at));
            return new BagDropPreview(target.Column, target.Row, target.Width, target.Height, fits);
        }

        if (to.InCarry)
        {
            var (carryColumn, carryRow) = PositionOf(bag, pointed);
            return new BagDropPreview(carryColumn, carryRow, 1, 1, fits);
        }

        // 拿起时点的那一格离左上角差几列几行；随身栏那一排里的只占 1 格，差的是零。
        var width = bag.GridWidth;
        var (dx, dy) = from.InCarry
            ? (0, 0)
            : ((from.Index % width) - (anchor.Index % width), (from.Index / width) - (anchor.Index / width));
        var footprint = bag.Items.Items[moving.ItemId].Footprint;
        var left = (to.Index % width) - dx;
        var top = (to.Index / width) - dy;
        var x0 = Math.Max(left, 0);
        var y0 = Math.Max(top, 0);
        var x1 = Math.Min(left + footprint.Width, width);
        var y1 = Math.Min(top + footprint.Height, bag.GridHeight);
        return new BagDropPreview(
            x0, BagPageLayout.CarryRows(bag.CarryCount, width) + y0, x1 - x0, y1 - y0, fits);
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

    /// <summary>背包里的一处在页上是第几格：先随身栏那一排，再网格（与 <see cref="BagControls.PlaceOf"/> 互逆）。</summary>
    private static int IndexOf(Bag bag, BagPlace place) => place.InCarry ? place.Index : bag.CarryCount + place.Index;

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
