namespace Tinderhearth.Rules.Foundation.Content;

/// <summary>
/// 把若干个 <see cref="IContentSource"/> 按顺序叠起来：后面的来源覆盖前面的同名条目。
/// </summary>
/// <remarks>
/// 这是 mod 与 DLC 的公共地基。玩法正典定的边界是「内容外置、规则不外置」，所以这里只管内容
/// 文件的叠加与来源追溯，不提供任何让 mod 改规则的入口。
///
/// 记住每条内容来自哪个来源是有意的：卸载 mod 之后要能列出「因为缺少 mod 而不可用」的角色与
/// 物品，那件事只能靠来源信息做，事后补要翻遍加载路径。
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
    /// 解析某个目录下的内容文件，整文件覆盖：同名文件由后登记的来源胜出。
    /// </summary>
    /// <remarks>
    /// 适用于「一个文件一个实体」的内容 —— 角色、宝物、配方、敌人。那样的内容里，一个 mod
    /// 想改某个角色，本来就该整份替换掉它。
    ///
    /// 不适用于文本表这种「一个文件很多条目」的内容，那种要用 <see cref="ResolveAll"/> 逐条目
    /// 合并。这个区分是实测撞出来的：mod 提供 <c>text/zh-CN.json</c> 时整文件覆盖会把基础文本
    /// 里其它键全部抹掉，于是一个只想改一句台词的 mod 会让界面上到处出现缺文本占位。
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
    /// 解析「一个文件一个实体」的内容并按标识建表。同一路径照旧整文件覆盖（见 <see cref="Resolve"/>）；
    /// 不同的文件用了同一个标识则当场抛，撞上的每一组连同各自的来源一次列全。
    /// </summary>
    /// <remarks>
    /// 标识是存档、mod 与别的内容找它用的键，一个标识只许对应一份定义。不拦的话，撞上之后后读到的那份
    /// 会顶掉前一份、或者进不了名册，而这不报错 —— 表现只是某个角色或作物不见了。角色的标识取名字的
    /// 拼音，同音的名字就会撞上；撞了怎么改在设计仓 production/界面绘制约定.md 的「文件放哪、叫什么」一节。
    ///
    /// 同一路径不算撞：那是 mod 有意整份替换，分不出是有意还是碰巧同名，所以这里也不拦；让它看得见归
    /// 设计仓台账 <c>ENG-32</c>。
    /// 报错里的文件按路径排，同一份内容每次报得都一样。
    /// </remarks>
    /// <param name="relativeDirectory">内容目录，例如 <c>characters</c>。</param>
    /// <param name="parse">把一条内容文件解析成定义。它抛的报错外面包一层，写上是哪一份、来自哪个来源。</param>
    /// <param name="idOf">从定义上取标识。</param>
    public IReadOnlyDictionary<string, T> ResolveById<T>(
        string relativeDirectory, Func<ContentEntry, T> parse, Func<T, string> idOf)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(idOf);

        var parsed = new List<(ContentEntry Entry, string Id, T Definition)>();
        foreach (var entry in Resolve(relativeDirectory).Values.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            var definition = ParseNamingTheFile(entry, parse);
            var id = idOf(definition);
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException($"{entry.RelativePath}（来自 {entry.SourceName}）没有标识");
            }

            parsed.Add((entry, id, definition));
        }

        var clashes = parsed
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"「{group.Key}」在 "
                + string.Join("、", group.Select(p => $"{p.Entry.RelativePath}（来自 {p.Entry.SourceName}）")))
            .ToList();
        if (clashes.Count > 0)
        {
            throw new InvalidDataException(
                $"{relativeDirectory} 下有标识被不止一份文件用了，一个标识只许对应一份定义：{string.Join("；", clashes)}");
        }

        return parsed.ToDictionary(p => p.Id, p => p.Definition, StringComparer.Ordinal);
    }

    /// <summary>解析一份。失败时报错里写上是哪一份、来自哪个来源，原来那条留作内层异常。</summary>
    /// <remarks>
    /// 缺键这类报错出自序列化器，它不知道读的是哪一份：实测一份作物定义缺了一个键，报错只说缺了哪个属性。
    /// 一个目录里有许多份时，不点名就只能一份份去试。
    /// </remarks>
    private static T ParseNamingTheFile<T>(ContentEntry entry, Func<ContentEntry, T> parse)
    {
        try
        {
            return parse(entry);
        }
        catch (Exception e)
        {
            throw new InvalidDataException($"{entry.RelativePath}（来自 {entry.SourceName}）读不出来：{e.Message}", e);
        }
    }

    /// <summary>
    /// 按加载顺序返回全部内容文件，不去重。同名文件会出现多次，先基础后 mod。
    /// </summary>
    /// <remarks>
    /// 给「一个文件很多条目」的内容用：调用方按顺序逐条目合并，后面的覆盖前面的同键条目，而
    /// 不覆盖整份文件。文本表是第一个这样的使用者。
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
/// <param name="SourceName">提供它的来源名。卸载 mod 之后的缺失提示要靠它。</param>
/// <param name="Text">文件正文。</param>
public readonly record struct ContentEntry(string RelativePath, string SourceName, string Text);
