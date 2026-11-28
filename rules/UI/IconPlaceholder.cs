using System.Globalization;

namespace Tinderhearth.Rules.UI;

/// <summary>图标还是「暂无」时格子里写什么：那一条名字的第一个字。</summary>
/// <remarks>
/// 口径在设计仓 production/界面绘制约定.md：内容图标没画之前用文字占位，不画一个通用的问号图标 —— 那样每一样都长得一样，
/// 背包里分不清谁是谁。「第一个字」按文本元素取，不按 char 取：名字里有拼成一个字的那几种（代理对、带组合符号的字）时，
/// 按 char 只截到半个，画出来是乱码。
/// </remarks>
public static class IconPlaceholder
{
    /// <summary>名字里没有可写的字时写的那一个。</summary>
    /// <remarks>名字是空串只在文本表那一条写空了时出现，那由载入时的文本核对报，这里只保证格子里不空着。</remarks>
    public const string Unnamed = "?";

    /// <summary>这条名字的第一个字；名字是空的或者只有空白时写 <see cref="Unnamed"/>。</summary>
    public static string TextFor(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? Unnamed : StringInfo.GetNextTextElement(trimmed);
    }
}
