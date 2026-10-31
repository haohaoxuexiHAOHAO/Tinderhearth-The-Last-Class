using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;

namespace Tinderhearth.Rules.Progression;

/// <summary>物品的一个类别：它是什么（药材、种子……）。一份数据文件一个类别，层级写在这里，不写在每件物品上。</summary>
/// <remarks>
/// 规则在设计仓 design/物品系统.md 的「物品定义的字段：只写「它是什么」，不写「它能干什么」」一节：一件物品只有一个类别，
/// 到哪儿都是它；定价里替代品那一层按更粗的一层分组，分组是一组类别值、由这里的层级推出来，不是每件物品身上的一份副本。
///
/// 加一个类别就是多放一份文件，不改代码。父类别指不到、或者层级绕成一个圈，载入时由 <see cref="ItemCatalog"/> 报。
/// </remarks>
public sealed record ItemCategory
{
    public const string ContentDirectory = "item_categories";

    /// <summary>稳定标识。物品定义按它引用。</summary>
    public required string Id
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("物品类别的 Id 不得为空", nameof(Id))
            : value;
    }

    /// <summary>名字在文本表里的键。</summary>
    public required string NameKey
    {
        get;
        init => field = TextKey.Require(value, $"物品类别的 {nameof(NameKey)}");
    }

    /// <summary>上一层是哪个类别；<c>null</c> 表示它在最顶层。</summary>
    /// <remarks>要显式写 <c>null</c>：不写这个键是漏填，载入被拒 —— 否则漏填的那一个会悄悄变成顶层类别。</remarks>
    public required string? ParentId
    {
        get;
        init => field = value is not null && string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("物品类别的 ParentId 要么是 null（顶层），要么是一个类别的标识", nameof(ParentId))
            : value;
    }

    public static ItemCategory Parse(string json, string whatForDiagnostics) =>
        ContentJson.Parse<ItemCategory>(json, whatForDiagnostics);
}
