using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Progression;

/// <summary>
/// 一个角色的定义，来自数据文件而不是代码（`ENG-5`）。
/// </summary>
/// <remarks>
/// 玩法正典把「全部内容一律数据外置」列为 mod 与 DLC 的公共地基，角色是其中第一项。
///
/// **注意 <see cref="DisplayNameKey"/> 是键不是名字**：角色的显示名走文本表，所以
/// 数据外置与文本外置这两条预留在这里是同一件事的两半。谁往这里塞一个中文名字面量，
/// 就等于同时破了两条。
///
/// 属性、成长与判定相关的字段**一个都不在这里** —— 那些归 `GP-2`，尚未设计。
/// </remarks>
/// <param name="Id">稳定标识，同时是名册与控制器登记表里的键。</param>
/// <param name="DisplayNameKey">显示名在文本表里的键。</param>
/// <param name="TraitIds">出生特质的标识列表；正典说特质上限 8 条，具体校验归特质系统。</param>
public sealed record CharacterDefinition(
    string Id,
    string DisplayNameKey,
    IReadOnlyList<string> TraitIds)
{
    public const string ContentDirectory = "characters";

    public static CharacterDefinition Parse(string json, string whatForDiagnostics) =>
        ContentJson.Parse<CharacterDefinition>(json, whatForDiagnostics);
}
