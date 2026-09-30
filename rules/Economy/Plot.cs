namespace Tinderhearth.Rules.Economy;

/// <summary>基地上一格地：开荒与种植共用的那台状态机。</summary>
/// <remarks>
/// 状态、流转、浇水那两个量、荒置退化与「一格要存什么」的权威都是设计仓 `design/地块系统.md`，
/// 本类不复述那几条理由。
///
/// **本类只算到「收上来几件」。** 归属、付酬与入库一律走生产系统那条管道，本类不碰容器、
/// 不碰品质（品质读种植那项生活技能），也不碰玩家怎么选中一格（那是界面侧的当前操作格）。
///
/// **它只存作物的标识，不存作物的定义。** 定义是内容、会被 mod 换掉，所以按天推进与收获都要
/// 调用方把当前那份 <see cref="CropDefinition"/> 传进来；传错一种作物会当场报错，不会按错的
/// 天数悄悄长下去。这与存档那条「存的一律是标识」同源。
///
/// **动作全部返回 <c>bool</c> 而不抛异常。** 状态不对是玩家可以碰到的正常结果（他对着一片没锄
/// 的地按了播种），不是缺陷。抛异常的只有「调用方传错了作物定义」这类真缺陷。
/// </remarks>
public sealed class Plot
{
    /// <summary>造一格地。**只许从没有作物的那几个状态起步**，有作物的状态必须经过播种。</summary>
    /// <remarks>
    /// 开局那一小块种植区是**已锄**的（正典：一小块已开垦的种植区），所以这个构造要容得下
    /// <see cref="PlotState.Tilled"/> —— 否则开局那几格只能靠代码替玩家锄一遍，而那会让
    /// 「谁把它锄的」在存档里说不清。
    /// </remarks>
    public Plot(PlotState initial = PlotState.Uncleared)
    {
        State = initial switch
        {
            PlotState.Uncleared or PlotState.Cleared or PlotState.Tilled => initial,
            _ => throw new ArgumentOutOfRangeException(
                nameof(initial),
                $"一格地只能从 {PlotState.Uncleared}、{PlotState.Cleared} 或 {PlotState.Tilled} 起步，"
                    + $"实际 {initial} —— 带作物的状态必须经过播种，否则作物标识与阶段都是空的"),
        };
    }

    /// <summary>这一格此刻的状态。</summary>
    public PlotState State { get; private set; }

    /// <summary>地里那种作物的标识；没有作物时为 <c>null</c>。</summary>
    /// <remarks>**枯死之后它仍然在** —— 显示层要靠它知道该画哪一种作物的枯死图。</remarks>
    public string? CropId { get; private set; }

    /// <summary>当前在第几个生长阶段（0 起）。</summary>
    public int Stage { get; private set; }

    /// <summary>在当前这一阶段已经过了几天。</summary>
    /// <remarks>
    /// 存「阶段 ＋ 这一阶段过了几天」而不是「从播种起过了几天」：后者在循环收获退回中途某一
    /// 阶段时要反算一次，而反算错了不报错，只表现为「这一茬长得不对」。
    /// </remarks>
    public int DaysInStage { get; private set; }

    /// <summary>今天浇过了吗。**每天结算时清掉**，所以湿的表现每天早上回到干。</summary>
    public bool WateredToday { get; private set; }

    /// <summary>这一茬累计浇过几天。**每次收获清零。**</summary>
    public int WateredDaysThisCrop { get; private set; }

    /// <summary>这一茬实际经过了几天。它是产量系数的分母。</summary>
    /// <remarks>第一茬从播种算起，之后每茬从上一次收获算起。到了待收就不再涨。</remarks>
    public int DaysThisCrop { get; private set; }

    /// <summary>已锄且空着连续多少天了。**有作物的那几格不计时。**</summary>
    public int DaysFallow { get; private set; }

    /// <summary>清掉石头、树木与杂草。</summary>
    public bool Clear()
    {
        if (State != PlotState.Uncleared)
        {
            return false;
        }

        State = PlotState.Cleared;
        return true;
    }

    /// <summary>锄开。**这是播种之前独立的一步**，不与清理合并。</summary>
    public bool Till()
    {
        if (State != PlotState.Cleared)
        {
            return false;
        }

        State = PlotState.Tilled;
        DaysFallow = 0;
        return true;
    }

    /// <summary>播种。只有已锄的地能种 —— **可耕格会被拒**。</summary>
    public bool Plant(CropDefinition crop)
    {
        ArgumentNullException.ThrowIfNull(crop);

        if (State != PlotState.Tilled)
        {
            return false;
        }

        State = PlotState.Planted;
        CropId = crop.Id;
        Stage = 0;
        DaysInStage = 0;
        DaysThisCrop = 0;
        WateredDaysThisCrop = 0;
        WateredToday = false;
        DaysFallow = 0;
        return true;
    }

    /// <summary>浇水。**只有正在长的作物浇得上**。</summary>
    /// <remarks>
    /// 空地与待收的地都浇不上：湿的状态每天早上重置，而分母在到达成熟那一刻就定住了 ——
    /// 对它们浇水不改变任何结果，允许一个没有后果的动作等于让玩家白花时间。
    ///
    /// **浇水当场就把累计加 1**，所以每日结算只需清掉当天那个标记，清在那一步的哪个位置都
    /// 不影响结果。同一天浇第二次不重复计数。
    /// </remarks>
    public bool Water()
    {
        if (State != PlotState.Planted || WateredToday)
        {
            return false;
        }

        WateredToday = true;
        WateredDaysThisCrop++;
        return true;
    }

    /// <summary>收获。一次性作物收完回到已锄，循环作物退回它声明的那一阶段。</summary>
    /// <param name="crop">这一格里那种作物的定义。</param>
    /// <param name="rule">收几件那几个量，由调用方给（本层没有默认值）。</param>
    /// <param name="count">收上来几件；没收成时为 0。</param>
    /// <remarks>
    /// 件数一律是**整数且至少 1 件**：与伤害那条「任何攻击都造成至少 1 点」同形。玩家看到的
    /// 不能是「收了 5.4 颗」，也不能是「收了 0 颗」。
    /// </remarks>
    public bool TryHarvest(CropDefinition crop, HarvestYieldRule rule, out int count)
    {
        ArgumentNullException.ThrowIfNull(crop);
        ArgumentNullException.ThrowIfNull(rule);
        RequireMatchingCrop(crop);

        count = 0;
        if (State != PlotState.Harvestable)
        {
            return false;
        }

        count = YieldCount(rule);

        if (crop.RegrowFromStage is int back)
        {
            State = PlotState.Planted;
            Stage = back;
        }
        else
        {
            State = PlotState.Tilled;
            CropId = null;
            Stage = 0;
        }

        DaysInStage = 0;
        DaysThisCrop = 0;
        WateredDaysThisCrop = 0;
        WateredToday = false;
        DaysFallow = 0;
        return true;
    }

    /// <summary>清掉枯株。**这一次清理不产任何材料、也不耗精力**，与开荒那次清理是两件事。</summary>
    public bool ClearWithered()
    {
        if (State != PlotState.Withered)
        {
            return false;
        }

        State = PlotState.Tilled;
        CropId = null;
        Stage = 0;
        DaysInStage = 0;
        DaysThisCrop = 0;
        WateredDaysThisCrop = 0;
        DaysFallow = 0;
        return true;
    }

    /// <summary>换季那一步：新季节不在这种作物声明的列表里就枯死。</summary>
    /// <remarks>
    /// **它必须排在 <see cref="AdvanceDay"/> 之前**（正典结算顺序里季节更替在作物生长之前）——
    /// 反了的话，换季那天的作物会先白长一天再枯死。
    ///
    /// 一条规则覆盖单季、跨季与全年，所以这里没有「是不是跨季」那种分支。
    /// </remarks>
    public void ApplySeasonChange(CropDefinition? crop, Season newSeason)
    {
        if (State is not (PlotState.Planted or PlotState.Harvestable))
        {
            return;
        }

        RequireCropForGrowth(crop);
        if (crop!.Seasons.Contains(newSeason))
        {
            return;
        }

        State = PlotState.Withered;
        Stage = 0;
        DaysInStage = 0;
        DaysThisCrop = 0;
        WateredDaysThisCrop = 0;
    }

    /// <summary>按天推进一天：生长、荒置计时，最后清掉当天那个浇水标记。</summary>
    /// <param name="crop">这一格里那种作物的定义；空地传 <c>null</c>。</param>
    /// <param name="fallowRevertDays">已锄的空格荒置几天退回可耕。天数归数值模型，本层不带默认值。</param>
    /// <remarks>
    /// 这几件事**全在每日结算「作物生长与枯死结算」那一步之内**，不新增结算步骤 —— 那串步序
    /// 一步不增是正典的硬约束，而它们都是「地块上的东西按天推进」。
    /// </remarks>
    public void AdvanceDay(CropDefinition? crop, int fallowRevertDays)
    {
        if (fallowRevertDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fallowRevertDays),
                $"荒置退化天数必须至少 1，实际 {fallowRevertDays}（缺配置也会得到 0）");
        }

        switch (State)
        {
            case PlotState.Planted:
                RequireCropForGrowth(crop);
                Grow(crop!);
                break;

            case PlotState.Tilled:
                DaysFallow++;
                if (DaysFallow >= fallowRevertDays)
                {
                    State = PlotState.Cleared;
                    DaysFallow = 0;
                }

                break;

            default:
                // 未清理、可耕、待收与待清都不按天走：前两个没有可推进的东西，
                // 后两个在等玩家动手（收或清），替他计时只会把「放着不管」变成惩罚。
                break;
        }

        WateredToday = false;
    }

    private void Grow(CropDefinition crop)
    {
        DaysInStage++;
        DaysThisCrop++;

        while (Stage < crop.RipeStage && DaysInStage >= crop.StageDays[Stage])
        {
            DaysInStage -= crop.StageDays[Stage];
            Stage++;
        }

        if (Stage >= crop.RipeStage)
        {
            State = PlotState.Harvestable;
        }
    }

    private int YieldCount(HarvestYieldRule rule)
    {
        var denominator = Math.Max(1, DaysThisCrop);
        var raw = (double)WateredDaysThisCrop / denominator;
        var factor = Math.Clamp(raw, rule.MinWaterFactor, rule.MaxWaterFactor);
        return Math.Max(1, (int)Math.Floor(rule.BaseCount * factor));
    }

    private void RequireCropForGrowth(CropDefinition? crop)
    {
        if (crop is null)
        {
            throw new ArgumentNullException(
                nameof(crop),
                $"这一格是 {State}、作物标识是 {CropId}，推进它要把那种作物的定义传进来");
        }

        RequireMatchingCrop(crop);
    }

    private void RequireMatchingCrop(CropDefinition crop)
    {
        if (CropId is not null && CropId != crop.Id)
        {
            throw new ArgumentException(
                $"这一格种的是 {CropId}，传进来的定义是 {crop.Id}", nameof(crop));
        }
    }
}
