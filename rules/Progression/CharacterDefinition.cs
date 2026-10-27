using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Progression;

/// <summary>
/// 一个角色的定义，来自数据文件而不是代码。
/// </summary>
/// <remarks>
/// 玩法正典把「全部内容一律数据外置」列为 mod 与 DLC 的公共地基，角色是其中第一项。
///
/// 注意 <see cref="DisplayNameKey"/> 是键不是名字：角色的显示名走文本表，所以数据外置与文本
/// 外置这两条预留在这里是同一件事的两半。谁往这里塞一个中文名字面量，就等于同时破了两条。
///
/// 属性、成长与判定相关的字段一个都不在这里 —— 那些写在设计仓 design/数值模型.md，等真有系统
/// 要用它们时再按需加，现在往里塞就是凭手感猜。
/// </remarks>
/// <param name="Id">稳定标识，同时是名册与控制器登记表里的键。</param>
/// <param name="DisplayNameKey">显示名在文本表里的键。</param>
/// <param name="TraitIds">
/// 出生特质的标识列表。条数没有上限，限制靠互斥组（同一组最多一条），具体校验归特质系统。
/// </param>
public sealed record CharacterDefinition(
    string Id,
    string DisplayNameKey,
    IReadOnlyList<string> TraitIds)
{
    public const string ContentDirectory = "characters";

    /// <summary>解析一份角色定义，并核标识的写法。</summary>
    /// <remarks>
    /// 标识取名字的拼音：全小写、不带声调、不分隔，同音撞了的那个在末尾接数字；取法在设计仓
    /// production/界面绘制约定.md 的「文件放哪、叫什么」一节。这里核的只是写法 —— 一串小写字母，末尾
    /// 可以接数字。核它是为了让「拼音相同」就等于「字符串相同」：混进大写、声调或分隔符的那一份，会和
    /// 另一份同音的绕开 <see cref="ContentCatalog.ResolveById"/> 的撞名检查，而那不报错。
    ///
    /// 是不是真的拼音判不了（一个英文单词也全是小写字母），那一半靠写的人。
    /// </remarks>
    public static CharacterDefinition Parse(string json, string whatForDiagnostics)
    {
        var definition = ContentJson.Parse<CharacterDefinition>(json, whatForDiagnostics);
        if (!IsWellFormedId(definition.Id))
        {
            throw new InvalidDataException(
                $"{whatForDiagnostics} 的标识「{definition.Id ?? "（没写）"}」写法不对：只许小写字母，"
                    + "同音撞了才在末尾接数字（例如 yunyi2）。取法在设计仓 production/界面绘制约定.md 的"
                    + "「文件放哪、叫什么」一节");
        }

        return definition;
    }

    /// <summary>一串小写字母，末尾可以接数字。</summary>
    private static bool IsWellFormedId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var letters = 0;
        while (letters < id.Length && char.IsAsciiLetterLower(id[letters]))
        {
            letters++;
        }

        return letters > 0 && id[letters..].All(char.IsAsciiDigit);
    }
}
