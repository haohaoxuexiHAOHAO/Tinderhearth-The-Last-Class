using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Economy;

/// <summary>一种作物的定义，来自数据文件而不是代码（`ENG-5`）。</summary>
/// <remarks>
/// 字段表与每条的理由在设计仓 `design/地块系统.md`，本类不复述。
///
/// **素材那一行不在这里。** 六张图（五个生长阶段各一张，加一张枯死）由作者在 Godot 里配，
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

    /// <summary>生长阶段的个数：种子、出苗、幼株、成株、成熟。**素材张数是它加一张枯死。**</summary>
    public const int StageCount = 5;

    /// <summary>成熟那一阶段的序号（0 起）。到了它这一格就是待收。</summary>
    public const int RipeStage = StageCount - 1;

    /// <summary>
    /// 要声明天数的阶段有几个。**它比 <see cref="StageCount"/> 少一个**，因为成熟是终态：
    /// 到了它就一直待收，等玩家来收或者换季枯死，没有「持续几天」这回事。
    /// </summary>
    /// <remarks>
    /// 给成熟那一阶段也留一个天数会多出一个**没有任何东西读它**的字段，而那种字段填错了不报错。
    /// </remarks>
    public const int TimedStageCount = RipeStage;

    /// <summary>稳定标识。</summary>
    public required string Id
    {
        get;
        init => field = NonEmpty(value, nameof(Id));
    }

    /// <summary>
    /// 种子、出苗、幼株、成株各持续几天，顺序即生长顺序，**各至少 1 天**；
    /// 成熟那一阶段不在其内（见 <see cref="TimedStageCount"/>）。
    /// 由此最短生长周期就是 <see cref="TimedStageCount"/> 天。
    /// </summary>
    public required IReadOnlyList<int> StageDays
    {
        get;
        init => field = CheckStageDays(value);
    }

    /// <summary>
    /// 收获后退回第几阶段（0 起）；<c>null</c> 表示**一次性作物**，收完地就空了。
    /// </summary>
    /// <remarks>
    /// **不许填成熟那一阶段**：退回去之后立刻又是待收，等于一次收获就能无限收下去。
    ///
    /// **再生间隔没有单独的字段**：退回阶段 N 之后照 <see cref="StageDays"/> 里第 N 项往前走，
    /// 而那一项的含义本来就是「从这一阶段到下一阶段要几天」。
    /// </remarks>
    public required int? RegrowFromStage
    {
        get;
        init => field = CheckRegrow(value);
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

    public static CropDefinition Parse(string json, string whatForDiagnostics) =>
        ContentJson.Parse<CropDefinition>(json, whatForDiagnostics);

    private static string NonEmpty(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"作物定义的 {field} 不得为空", field)
            : value;

    private static IReadOnlyList<int> CheckStageDays(IReadOnlyList<int> value)
    {
        if (value is null || value.Count != TimedStageCount)
        {
            throw new ArgumentException(
                $"作物定义的 {nameof(StageDays)} 必须恰好 {TimedStageCount} 项"
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

    private static int? CheckRegrow(int? value) => value switch
    {
        null => null,
        >= 0 and < RipeStage => value,
        _ => throw new ArgumentOutOfRangeException(
            nameof(RegrowFromStage),
            $"作物定义的 {nameof(RegrowFromStage)} 要么是 null（一次性），"
                + $"要么落在 0 到 {RipeStage - 1} 之间（退回成熟那一阶段等于无限收获），实际 {value}"),
    };

    private static IReadOnlyList<Season> CheckSeasons(IReadOnlyList<Season> value) =>
        value is { Count: > 0 }
            ? value
            : throw new ArgumentException(
                $"作物定义的 {nameof(Seasons)} 至少要有一个季节，否则它种下去当天就枯死",
                nameof(Seasons));
}
