using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;

namespace Tinderhearth.Rules.Progression;

/// <summary>物品的两条轴：稀有度管难不难拿到手，品质管拿到手的这一个做得多好。</summary>
public enum ItemTierAxis
{
    Rarity,
    Quality,
}

/// <summary>两条轴上的一档。一份数据文件一档，加一档不改代码。</summary>
/// <remarks>
/// 每一档声明哪几格在设计仓 design/物品系统.md 的「两条轴的档位各是一张在册的声明表」一节，本类不复述是什么、只管缺了怎么办。
/// 读档位的代码一律遍历 <see cref="ItemCatalog"/> 里在册的那几档，不按序号取某一档：排第几只由
/// <see cref="SortOrder"/> 定，而那一格允许重复（品质顶上那一级有两个值，传奇与变异）。
///
/// 两条轴声明的是同一组格子，所以共用这一个类型。档位不管宝物带几条词条：词条全部随机
/// （工作区根 desc/经营规则.md 的「词条」），条数不跟着稀有度定死。
/// </remarks>
public sealed record ItemTier
{
    /// <summary>稳定标识，存档与 mod 按它引用。</summary>
    public required string Id
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("档位的 Id 不得为空", nameof(Id))
            : value;
    }

    /// <summary>显示名在文本表里的键。</summary>
    public required string NameKey
    {
        get;
        init => field = TextKey.Require(value, $"档位的 {nameof(NameKey)}");
    }

    /// <summary>高低由它定，越大越高。允许两档相同：同一级、不同名。</summary>
    public required int SortOrder { get; init; }

    /// <summary>基础价系数；<c>null</c> 表示还没给值。</summary>
    /// <remarks>
    /// 值归设计仓 design/数值模型.md，系数值归设计仓台账 GP-7；现在还没有，所以数据里写显式的 <c>null</c>：不写这个键是漏填、
    /// 载入被拒，<c>null</c> 是「还没给」。读它的是定价，那一侧落地时读到 <c>null</c> 要当场报错，不当成 1。
    /// 给了值就必须为正；品质同一级的几档共用一个系数，由 <see cref="ItemCatalog"/> 核。
    /// </remarks>
    public required double? BasePriceFactor
    {
        get;
        init => field = value is null or > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(BasePriceFactor), $"档位的 {nameof(BasePriceFactor)} 要么是 null（还没给值），要么为正，实际 {value}");
    }

    /// <summary>显示色：色板里一档的名字（例如 <c>ember 5</c>）。在不在色板里、品质那几档的色带撞不撞，由 <see cref="ItemCatalog"/> 核。</summary>
    public required string DisplayColor
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"档位的 {nameof(DisplayColor)} 要填色板里一档的名字", nameof(DisplayColor))
            : value;
    }

    /// <summary>这一轴的档位放在哪个内容目录。</summary>
    public static string ContentDirectoryOf(ItemTierAxis axis) => axis switch
    {
        ItemTierAxis.Rarity => "item_rarities",
        ItemTierAxis.Quality => "item_qualities",
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "物品没有第三条轴"),
    };

    /// <summary>解析一档。缺哪一格由解析当场报错（那几格都是 <c>required</c>）。</summary>
    public static ItemTier Parse(string json, string whatForDiagnostics) =>
        ContentJson.Parse<ItemTier>(json, whatForDiagnostics);
}
