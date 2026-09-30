using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Economy;

/// <summary>一种作物的定义，来自数据文件而不是代码（`ENG-5`）。</summary>
/// <remarks>
/// 字段表与每条的理由在设计仓 `design/地块系统.md`，本类不复述。
///
/// **素材那一行不在这里。** 那几张图（每个阶段各一张，加一张枯死，所以张数按它声明的阶段数算）由作者在 Godot 里配，
/// 与角色逐帧素材同一个分工（`ADR-0009`）。所以「缺哪张报错」是引擎层的载入校验，
/// 规则层看不见任何路径。
///
/// **收几件与浇水系数不在这里。** 那两个量归数值模型、现在还没有值，所以它们由调用方传
/// <see cref="HarvestYieldRule"/> 进来 —— 代码不留能用的默认值（`ADR-0009`）。本类只持有
/// 阶段天数这一类**由内容决定**的数。
///
/// **本类刻意不用位置参数 record，而是 <c>required</c> 属性。** 这是对 `GameConfig` 那条实测
/// 结论（位置参数 record 配 <c>System.Text.Json</c> 时缺字段不报错、拿 <c>default</c> 填）的
/// 一处必要偏离：<see cref="RegrowFromStage"/> 的 <c>null</c> **是合法值**（表示一次性作物），
/// 所以「缺字段」与「显式写 null」用构造校验区分不出来。<c>required</c> 让缺键这件事由
/// 反序列化器直接报错，而 <c>null</c> 照旧是一个要显式写出来的选择。
/// </remarks>
public sealed record CropDefinition
{
    public const string ContentDirectory = "crops";

    /// <summary>一种作物至少要有几个计时阶段。</summary>
    /// <remarks>
    /// 阶段序列是「声明的那几个计时阶段 ＋ 成熟」，**第一个必须是种子、最后一个必须是成熟**，
    /// 所以最少两个阶段：种子 → 成熟。计时阶段一个都没有的声明被拒 —— 那样的作物播下去当天就待收。
    /// </remarks>
    public const int MinTimedStages = 1;

    /// <summary>稳定标识。</summary>
    public required string Id
    {
        get;
        init => field = NonEmpty(value, nameof(Id));
    }

    /// <summary>
    /// 每个计时阶段各持续几天，顺序即生长顺序，**各至少 1 天**；
    /// 项数就是这一条作物有几个计时阶段，**由它自己声明**（至少 <see cref="MinTimedStages"/> 项）。
    /// 成熟那一阶段不在其内 —— 它是终态、不带天数。
    /// </summary>
    /// <remarks>
    /// 项数不写死，所以三天成熟的速生菜与慢熟的药草各按自己的阶段数声明；
    /// 一条作物最快几天成熟由它自己的这一项算出来（<see cref="DaysToFirstRipe"/>），
    /// **没有一个全局的「最短生长周期」** —— 那个数只会被抄到别处去然后抄错。
    /// </remarks>
    public required IReadOnlyList<int> StageDays
    {
        get;
        init => field = CheckStageDays(value);
    }

    /// <summary>成熟那一阶段的序号（0 起）。**它由这一条作物自己的阶段数算出来**，不是全局常量。</summary>
    /// <remarks>阶段序列是「计时阶段 ＋ 成熟」，所以成熟的序号就等于计时阶段的个数。</remarks>
    public int RipeStage => StageDays.Count;

    /// <summary>这一条作物一共有几个阶段。**素材张数是它加一张枯死。**</summary>
    public int StageCount => StageDays.Count + 1;

    /// <summary>
    /// 收获后退回第几阶段（0 起）；<c>null</c> 表示**一次性作物**，收完地就空了。
    /// </summary>
    /// <remarks>
    /// **不许填成熟那一阶段或更后**：退回去之后立刻又是待收，等于一次收获就能无限收下去。
    /// 那条上界要读 <see cref="RipeStage"/>，而它由 <see cref="StageDays"/> 算出来 ——
    /// **跨字段的校验在 <c>init</c> 里做不到**（属性赋值顺序不保证），所以它落在 <see cref="Parse"/>。
    /// 内容一律从数据文件来（`ENG-5`），那里是唯一的真实入口；直接 <c>new</c> 只出现在测试里。
    ///
    /// **再生间隔没有单独的字段**：退回阶段 N 之后照 <see cref="StageDays"/> 里第 N 项往前走，
    /// 而那一项的含义本来就是「从这一阶段到下一阶段要几天」。
    /// </remarks>
    public required int? RegrowFromStage
    {
        get;
        init => field = CheckRegrowNonNegative(value);
    }

    /// <summary>它属于哪几个季节。**换季那天新季节不在其中就枯死。**</summary>
    /// <remarks>
    /// 单季、跨季与全年是同一条判定的三种填法，所以**没有「是不是跨季」那种开关**。
    /// 一条纪律现在还判不到：**跨季的不能是粮食**（正典）—— 等物品类别里有粮食那一类，
    /// 「多个季节 ＋ 产出粮食」就该在载入时被拒。
    /// </remarks>
    public required IReadOnlyList<Season> Seasons
    {
        get;
        init => field = CheckSeasons(value);
    }

    /// <summary>产出哪一种物品。**存的是标识**，物品定义在物品系统那一侧。</summary>
    public required string YieldItemId
    {
        get;
        init => field = NonEmpty(value, nameof(YieldItemId));
    }

    /// <summary>这种作物收获之后会退回生长中途、能反复收吗？</summary>
    public bool Regrows => RegrowFromStage is not null;

    /// <summary>从播种到第一次成熟要几天。</summary>
    public int DaysToFirstRipe => StageDays.Sum();

    /// <summary>从退回的那一阶段长回成熟要几天；一次性作物没有这个量。</summary>
    public int? DaysToRegrow =>
        RegrowFromStage is int back ? StageDays.Skip(back).Sum() : null;

    public static CropDefinition Parse(string json, string whatForDiagnostics)
    {
        var crop = ContentJson.Parse<CropDefinition>(json, whatForDiagnostics);
        crop.CheckRegrowBelowRipe();
        return crop;
    }

    /// <summary>跨字段校验：退回的那一阶段要落在成熟之前。理由见 <see cref="RegrowFromStage"/>。</summary>
    private void CheckRegrowBelowRipe()
    {
        if (RegrowFromStage is int back && back >= RipeStage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RegrowFromStage),
                $"作物定义 {Id} 的 {nameof(RegrowFromStage)} 是 {back}，而成熟那一阶段是 {RipeStage}"
                    + "：退回成熟或更后等于一次收获就能无限收下去");
        }
    }

    private static string NonEmpty(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"作物定义的 {field} 不得为空", field)
            : value;

    private static IReadOnlyList<int> CheckStageDays(IReadOnlyList<int> value)
    {
        if (value is null || value.Count < MinTimedStages)
        {
            throw new ArgumentException(
                $"作物定义的 {nameof(StageDays)} 至少要有 {MinTimedStages} 项"
                    + $"（成熟那一阶段是终态、不带天数），实际 {value?.Count.ToString() ?? "缺失"}",
                nameof(StageDays));
        }

        var bad = Enumerable.Range(0, value.Count).FirstOrDefault(i => value[i] < 1, -1);
        return bad < 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(StageDays),
                $"作物定义的 {nameof(StageDays)} 第 {bad} 项必须至少 1 天，实际 {value[bad]}");
    }

    private static int? CheckRegrowNonNegative(int? value) => value switch
    {
        null or >= 0 => value,
        _ => throw new ArgumentOutOfRangeException(
            nameof(RegrowFromStage),
            $"作物定义的 {nameof(RegrowFromStage)} 要么是 null（一次性），要么不为负，实际 {value}"),
    };

    private static IReadOnlyList<Season> CheckSeasons(IReadOnlyList<Season> value) =>
        value is { Count: > 0 }
            ? value
            : throw new ArgumentException(
                $"作物定义的 {nameof(Seasons)} 至少要有一个季节，否则它种下去当天就枯死",
                nameof(Seasons));
}
