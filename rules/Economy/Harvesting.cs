using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>主角在地里收一株的结果。</summary>
public enum HarvestStatus
{
    /// <summary>收上来了，进了背包。</summary>
    Stored,

    /// <summary>背包放不下，整份拒收：那一格照旧待收，一样不变。</summary>
    BagFull,

    /// <summary>这一格现在不是待收，什么都没发生。</summary>
    NotHarvestable,
}

/// <summary>收一株的结果，加上那一份走生产那条管道走成了什么样。</summary>
/// <param name="Status">收成了、放不下，还是不是待收。</param>
/// <param name="Delivery">那一份走完管道的结果；放不下时 <c>Stored</c> 是 <c>false</c>、里面是本该收上来的那一份。不是待收时是 <c>null</c>。</param>
public readonly record struct HarvestAttempt(HarvestStatus Status, ProductionOutcome? Delivery);

/// <summary>主角亲手收获那一道闸：先算收上来什么，经生产那条管道放进他的背包，放进去了才动地块。</summary>
/// <remarks>
/// 背包满了收不进时那一株还留在地里（设计仓 design/背包系统.md 的「满了怎么办：不自动丢弃，四个入口各有一条明确的拒绝路径」一节，
/// 主角拾取那一行在地里的样子）：拒绝不许吞掉世界状态，所以收成先交给管道，管道入库成了才改那一格。
/// 背包那一侧是整份拒收、一件不变，地块那一侧是先算不改，两边都没动过，放不下的那一下就什么都没发生。
/// </remarks>
public static class Harvesting
{
    /// <summary>变了的那一株收上来全是变异品质（设计仓 decisions/ADR-0030-作物成熟时按几率变异.md）。</summary>
    public const string MutatedQualityId = "mutated";

    /// <summary>没变的那一株收上来的品质。</summary>
    /// <remarks>
    /// 这是占位：设计仓 design/地块系统.md 写的是读种植那项生活技能，而技能还不存在。寻常是「日常制作与采集的常态」
    /// （设计仓 design/物品系统.md 的「品质五级，顶上那一级有两个名字」一节）；技能落地时换成读技能。
    /// 设计仓台账 GP-45
    /// </remarks>
    public const string UnmutatedQualityId = "common";

    /// <summary>收获要用的那两档品质在不在册：缺一档当场抛。启动时核一次，不等第一次收获才发现。</summary>
    public static void RequireQualitiesIn(ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var missing = new[] { MutatedQualityId, UnmutatedQualityId }
            .Where(id => items.Qualities.All(q => q.Id != id))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                $"收获要用的品质「{string.Join("」「", missing)}」不在册 —— 变了的那一株收上来是「{MutatedQualityId}」，"
                    + $"没变的是「{UnmutatedQualityId}」");
        }
    }

    /// <summary>主角亲手收这一格。</summary>
    /// <param name="plot">那一格地。</param>
    /// <param name="crop">这一格里那种作物的定义。</param>
    /// <param name="rule">算收几件要用的那几个量。</param>
    /// <param name="protagonistBag">主角的背包。</param>
    public static HarvestAttempt ByProtagonist(Plot plot, CropDefinition crop, HarvestYieldRule rule, Bag protagonistBag)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(crop);
        ArgumentNullException.ThrowIfNull(protagonistBag);

        // 产出：这一株收上来几件、什么品质。只算，不改这一格。
        if (!plot.PeekHarvest(crop, rule, out var harvest))
        {
            return new HarvestAttempt(HarvestStatus.NotHarvestable, null);
        }

        var goods = new ProducedGoods(
            crop.YieldItemId, harvest.Mutated ? MutatedQualityId : UnmutatedQualityId, harvest.Count);

        // 归属、付酬与入库在管道里走。放不下的话背包一件不变，这一格也还没动过。
        var delivery = ProductionPipeline.Deliver(Producer.Protagonist, goods, protagonistBag);
        if (!delivery.Stored)
        {
            return new HarvestAttempt(HarvestStatus.BagFull, delivery);
        }

        // 进了背包才改这一格。算的那一半与上面是同一处，收出来的必定是同一份；对不上是代码的错，不是放不下。
        if (!plot.TryHarvest(crop, rule, out var committed) || committed != harvest)
        {
            throw new InvalidOperationException(
                $"这一格先算出来的是 {harvest}，真收的时候却是 {committed} —— 算与改分叉了");
        }

        return new HarvestAttempt(HarvestStatus.Stored, delivery);
    }
}
