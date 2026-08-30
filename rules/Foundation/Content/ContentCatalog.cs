namespace Tinderhearth.Rules.Foundation.Content;

/// <summary>
/// 把若干个 <see cref="IContentSource"/> 按顺序叠起来：后面的来源覆盖前面的同名条目。
/// </summary>
/// <remarks>
/// 这是 mod 与 DLC 的公共地基（`ENG-5`）。玩法正典定的边界是「内容外置、规则不外置」——
/// 所以这里只管内容文件的叠加与来源追溯，不提供任何让 mod 改规则的入口。
///
/// **记住每条内容来自哪个来源**是有意的：`ENG-7` 要求卸载 mod 后能列出「因缺少 mod 而
/// 不可用」的角色与物品，那件事只能靠来源信息做，事后补要翻遍加载路径。
/// </remarks>
public sealed class ContentCatalog
{
    private readonly List<IContentSource> _sources = [];

    /// <summary>按加载顺序登记来源。先登记的是基础内容，后登记的覆盖它。</summary>
    public void AddSource(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
    }

    public IReadOnlyList<IContentSource> Sources => _sources;

    /// <summary>
    /// 解析某个目录下的内容文件，**整文件覆盖**：同名文件由后登记的来源胜出。
    /// </summary>
    /// <remarks>
    /// 适用于「一个文件一个实体」的内容 —— 角色、宝物、配方、敌人。那种形状下一个 mod
    /// 想改某个角色，本来就该整份替换掉它。
    ///
    /// **不适用于文本表这种「一个文件很多条目」的形状**，那种要用 <see cref="ResolveAll"/>
    /// 逐条目合并。这个区分是实测撞出来的：mod 提供 <c>text/zh-CN.json</c> 时整文件覆盖
    /// 会把基础文本里其它键**全部抹掉**（实测 5 条变 3 条），于是一个只想改一句台词的
    /// mod 会让界面上到处出现缺文本占位。
    /// </remarks>
    public IReadOnlyDictionary<string, ContentEntry> Resolve(string relativeDirectory)
    {
        var resolved = new Dictionary<string, ContentEntry>(StringComparer.Ordinal);
        foreach (var entry in ResolveAll(relativeDirectory))
        {
            resolved[entry.RelativePath] = entry;
        }

        return resolved;
    }

    /// <summary>
    /// 按加载顺序返回全部内容文件，**不去重**。同名文件会出现多次，先基础后 mod。
    /// </summary>
    /// <remarks>
    /// 给「一个文件很多条目」的内容用：调用方按顺序逐条目合并，后面的覆盖前面的**同键**
    /// 条目，而不覆盖整份文件。文本表是第一个这样的使用者。
    /// </remarks>
    public IReadOnlyList<ContentEntry> ResolveAll(string relativeDirectory)
    {
        var entries = new List<ContentEntry>();
        foreach (var source in _sources)
        {
            foreach (var path in source.List(relativeDirectory))
            {
                entries.Add(new ContentEntry(path, source.Name, source.ReadAllText(path)));
            }
        }

        return entries;
    }
}

/// <summary>一条内容文件，连同它来自哪个来源。</summary>
/// <param name="RelativePath">内容的相对路径，同时是它的稳定标识。</param>
/// <param name="SourceName">提供它的来源名，供 `ENG-7` 的缺失提示使用。</param>
/// <param name="Text">文件正文。</param>
public readonly record struct ContentEntry(string RelativePath, string SourceName, string Text);
