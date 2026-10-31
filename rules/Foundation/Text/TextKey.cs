namespace Tinderhearth.Rules.Foundation.Text;

/// <summary>文本表里一个键的写法：几段 ASCII 字母、数字与下划线，用点连起来，至少两段，第一段以字母开头。</summary>
/// <remarks>
/// 内容定义里填名称与描述的那几格存的是键，不是中文串（设计仓 design/物品系统.md 那张字段表）。把中文直接写进去，
/// 文本表里查不到它、界面上显成缺文本的占位，而载入不报错 —— 所以载入时按写法判：键里没有中文、空格与标点。
///
/// 判的只是写法。键在不在文本表里由启动时那一次核对报（<see cref="TextCatalog.MissingKeys"/>），那一条缺了不停游戏，
/// 理由在 <see cref="TextCatalog"/>。
/// </remarks>
public static class TextKey
{
    /// <summary>这一串是不是键的写法。</summary>
    public static bool IsWellFormed(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var segments = key.Split('.');
        return segments.Length >= 2
            && char.IsAsciiLetter(segments[0][0])
            && segments.All(s => s.Length > 0 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'));
    }

    /// <summary>是键就原样返回，不是就抛，并说清是哪一格、填的是什么。</summary>
    /// <param name="value">那一格填的值。</param>
    /// <param name="owner">报错里写的那一格，例如「物品定义的 NameKey」。</param>
    public static string Require(string value, string owner) =>
        IsWellFormed(value)
            ? value
            : throw new ArgumentException(
                $"{owner} 要填文本表里的一个键（几段字母、数字与下划线用点连起来，例如 item.crop_01_seed.name），"
                    + $"实际是「{value}」—— 名字与描述的文字只写在文本表里");
}
