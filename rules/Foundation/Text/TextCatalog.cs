namespace Tinderhearth.Rules.Foundation.Text;

/// <summary>
/// 面向玩家的文本一律按键取值，代码里不写死任何玩家看得见的字符串。
/// </summary>
/// <remarks>
/// 这条预留现在的成本近乎为零，事后补要翻遍代码。它同时兑现了母语审校那条要求：文本进了数据
/// 文件，译者与审校才有东西可看（禁译的那些术语在设计仓 canon/characters/人物.md）。
///
/// 缺键时返回一个显眼的占位标记，既不抛异常也不返回空串：抛异常会让一句漏翻的台词崩掉整个
/// 场景，空串会让漏翻悄悄消失，而漏翻必须看得见才会被修。
/// </remarks>
public sealed class TextCatalog
{
    private readonly Dictionary<string, string> _entries;

    public TextCatalog(IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = new Dictionary<string, string>(entries, StringComparer.Ordinal);
    }

    public static TextCatalog Empty { get; } = new(new Dictionary<string, string>());

    /// <summary>
    /// 按顺序把多份文本表逐键合并成一份，后面的覆盖前面的同键条目。
    /// </summary>
    /// <remarks>
    /// 文本必须逐键合并而不是整文件覆盖：一份文本表里有很多条目，mod 通常只想改其中一两句。
    /// 整文件覆盖会把它没提到的键全部抹掉（实测过），界面上就会冒出一片缺文本占位。
    ///
    /// 这也顺带给了 mod 一个便宜的用法：只写它要改的那几句，不必抄一份完整文本表 —— 抄一份
    /// 的话，基础文本每次更新它都会落后。
    /// </remarks>
    public static TextCatalog Merge(IEnumerable<IReadOnlyDictionary<string, string>> tables)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            foreach (var (key, value) in table)
            {
                merged[key] = value;
            }
        }

        return new TextCatalog(merged);
    }

    public int Count => _entries.Count;

    public bool Has(string key) => _entries.ContainsKey(key);

    /// <summary>取文本。缺键时返回 <c>◆缺文本:key◆</c> 这样的占位，界面上一眼能看到。</summary>
    public string this[string key] =>
        _entries.TryGetValue(key, out var value) ? value : $"◆缺文本:{key}◆";

    /// <summary>列出所有缺失的键。给验收用 —— 让「漏翻了几条」变成一个看得见的数字。</summary>
    public IReadOnlyList<string> MissingKeys(IEnumerable<string> requiredKeys) =>
        [.. requiredKeys.Where(k => !_entries.ContainsKey(k))];
}
