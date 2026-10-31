namespace Tinderhearth.Rules.Economy;

/// <summary>一份产出：哪一样物品、什么品质、几件。</summary>
/// <param name="ItemId">哪一样物品。</param>
/// <param name="QualityId">什么品质；那一样声明了无品质时是 <c>null</c>。</param>
/// <param name="Count">几件，至少 1。</param>
public readonly record struct ProducedGoods(string ItemId, string? QualityId, int Count);

/// <summary>谁干的：管道第一个环节问的那一格。</summary>
/// <remarks>
/// 现在只有主角亲手这一种。派工的学生与设备落地时各加一种，归属、付酬与入库那三格跟着各答一次。
/// 设计仓台账 GP-28
/// </remarks>
public enum Producer
{
    /// <summary>主角亲手干的。</summary>
    Protagonist,
}

/// <summary>产出归谁。</summary>
/// <remarks>
/// 现在只有主角这一种：基地产出归主角（设计仓 canon/gameplay/角色与成长.md 的「战利品归属」一节）。
/// 钓到的鱼归钓的人那一种，跟钓鱼落地。
/// </remarks>
public enum GoodsOwner
{
    /// <summary>归主角。</summary>
    Protagonist,
}

/// <summary>一份产出走完那四个环节的结果：产出了什么、归谁、付没付酬、入没入库。</summary>
/// <param name="Goods">产出了什么。</param>
/// <param name="Owner">归谁。</param>
/// <param name="Paid">付没付酬。</param>
/// <param name="Stored">入没入库。放不下就是 <c>false</c>：整份拒收，那一份留在原处，由调用方让它留着。</param>
public readonly record struct ProductionOutcome(ProducedGoods Goods, GoodsOwner Owner, bool Paid, bool Stored);

/// <summary>基地里各条渠道共用的那一条产出管道：产出 → 归属 → 付酬 → 入库，顺序固定，四个环节都不许跳。</summary>
/// <remarks>
/// 规则在设计仓 design/生产系统.md 的「一条管道，四个环节，顺序固定」一节，本类不复述理由。产出那一环由调用方算好交进来
/// （地里收上来几件、什么品质由地块那一侧算）；归属排在付酬之前 —— 先知道东西归谁，才谈得上要不要给钱。
///
/// 收获不许绕过它直接放进背包：将来清理物与派工产出也从这里进，入库只有这一处。
/// </remarks>
public static class ProductionPipeline
{
    /// <summary>一份产出走完四个环节。</summary>
    /// <param name="producer">谁干的。</param>
    /// <param name="goods">产出了什么。</param>
    /// <param name="protagonistBag">主角的背包：归主角、主角亲手干的那一份入库进这里。</param>
    public static ProductionOutcome Deliver(Producer producer, ProducedGoods goods, Bag protagonistBag)
    {
        ArgumentNullException.ThrowIfNull(protagonistBag);

        // 归属：读正典那条，不在这里另定。
        var owner = producer switch
        {
            Producer.Protagonist => GoodsOwner.Protagonist,
            _ => throw new ArgumentOutOfRangeException(nameof(producer), producer, "谁干的多了一种，归属这里还没有答"),
        };

        // 付酬：产出归主角、干活的又不是主角，才付。主角亲手干的不付。
        var paid = owner == GoodsOwner.Protagonist && producer != Producer.Protagonist;

        // 入库：主角亲手干的、归主角的进主角的背包。放不下整份拒收，背包一件不变。
        var stored = protagonistBag.TryAdd(goods.ItemId, goods.QualityId, goods.Count);

        return new ProductionOutcome(goods, owner, paid, stored);
    }
}
