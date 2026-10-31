using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;

namespace Tinderhearth.Rules.Progression;

/// <summary>一种物品的定义，来自数据文件而不是代码。一份文件一种物品。</summary>
/// <remarks>
/// 字段表在设计仓 design/物品系统.md 的「物品定义的字段：只写「它是什么」，不写「它能干什么」」一节，本类不复述每一格是什么。
///
/// **图标那两格不在这里。** 每个物品一份图标资源，由作者在 Godot 里配，资源上写它是哪个物品 —— 与作物素材同一个做法，
/// 规则层看不见图片；缺不缺、重不重在界面那一侧载入时一次全报（设计仓台账 <c>UI-63</c>）。**品质也不在这里**：它是
/// 实例属性，每一件各带一个，定义只声明这种东西有没有品质。
///
/// 用 <c>required</c> 属性的理由同 <see cref="Economy.CropDefinition"/>：位置参数 record 缺字段不报错，会拿 <c>default</c>
/// 填。这里好几格的 <c>null</c> 是合法值（还没给值的价、没有使用效果），只有 <c>required</c> 分得出「没写这个键」与
/// 「写了 <c>null</c>」；两个开关与「有无品质」也一样，<c>false</c> 是合法值，缺了不能悄悄落成它。
/// </remarks>
public sealed record ItemDefinition
{
    public const string ContentDirectory = "items";

    /// <summary>稳定标识。存档、作物与 mod 都按它引用；玩家看不到它。</summary>
    public required string Id
    {
        get;
        init => field = NonEmpty(value, nameof(Id));
    }

    /// <summary>名字在文本表里的键。</summary>
    public required string NameKey
    {
        get;
        init => field = TextKey.Require(value, $"物品定义的 {nameof(NameKey)}");
    }

    /// <summary>描述在文本表里的键。</summary>
    public required string DescriptionKey
    {
        get;
        init => field = TextKey.Require(value, $"物品定义的 {nameof(DescriptionKey)}");
    }

    /// <summary>它是什么。指一个在册的类别，在不在册由 <see cref="ItemCatalog"/> 核。</summary>
    public required string CategoryId
    {
        get;
        init => field = NonEmpty(value, nameof(CategoryId));
    }

    /// <summary>它的稀有度，同一种物品只有一个。指一个在册的档，在不在册由 <see cref="ItemCatalog"/> 核。</summary>
    public required string RarityId
    {
        get;
        init => field = NonEmpty(value, nameof(RarityId));
    }

    /// <summary>这种东西有没有品质这条轴。没有的（种子、典籍、任务物品）只按种类分堆，也不画品质。</summary>
    public required bool HasQuality { get; init; }

    /// <summary>一格装多少件。值归设计仓 design/数值模型.md，按种类给。</summary>
    public required int StackLimit
    {
        get;
        init => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(StackLimit), $"物品定义的 {nameof(StackLimit)} 必须为正，实际 {value}");
    }

    /// <summary>基础价的量级，以铜计；<c>null</c> 表示还没给值。</summary>
    /// <remarks>
    /// 值归设计仓 design/数值模型.md，还没有值的写显式的 <c>null</c>，理由同 <see cref="ItemTier.BasePriceFactor"/>：
    /// 不写这个键是漏填。读它的是定价，那一侧落地时读到 <c>null</c> 要当场报错。
    /// </remarks>
    public required int? BasePriceCopper
    {
        get;
        init => field = value is null or > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(BasePriceCopper), $"物品定义的 {nameof(BasePriceCopper)} 要么是 null（还没给值），要么为正，实际 {value}");
    }

    /// <summary>能不能丢。任务物品关掉它，挡住「丢了之后走不下去」。</summary>
    public required bool Discardable { get; init; }

    /// <summary>能不能卖。理由同 <see cref="Discardable"/>。</summary>
    public required bool Sellable { get; init; }

    /// <summary>用掉它触发哪一条效果；<c>null</c> 表示它不能直接用。</summary>
    /// <remarks>
    /// 效果本身在状态效果那一侧的数据里，这里只存引用。那一侧现在还没有数据表（设计仓台账 <c>GP-18</c>），填了的引用
    /// 一条都解析不到，所以现在只许 <c>null</c>：放过它，「能用」的物品用下去什么都不发生，而那不报错。
    /// </remarks>
    public required string? UseEffectId
    {
        get;
        init => field = value is null
            ? null
            : throw new ArgumentException(
                $"物品定义的 {nameof(UseEffectId)} 现在只能填 null：状态效果那一侧还没有数据表，"
                    + $"「{value}」解析不到任何效果",
                nameof(UseEffectId));
    }

    /// <summary>
    /// 用掉之前要不要先确认一次。判据在设计仓 decisions/ADR-0026-一道确认由误触与收益的落差决定.md，由作者逐件判。
    /// </summary>
    public required bool ConfirmBeforeUse { get; init; }

    /// <summary>这种物品要在文本表里有的那几条。启动时逐条核，缺了打一条错误。</summary>
    public IReadOnlyList<string> TextKeys => [NameKey, DescriptionKey];

    public static ItemDefinition Parse(string json, string whatForDiagnostics) =>
        ContentJson.Parse<ItemDefinition>(json, whatForDiagnostics);

    private static string NonEmpty(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"物品定义的 {field} 不得为空", field)
            : value;
}
