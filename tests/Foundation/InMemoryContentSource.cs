using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Tests.Foundation;

/// <summary>
/// 纯内存的内容来源，给测试用。不碰磁盘、不需要引擎 —— 这正是把 I/O 挡在规则层外面
/// 换来的好处。
/// </summary>
internal sealed class InMemoryContentSource(string name, Dictionary<string, string> files)
    : IContentSource
{
    public string Name { get; } = name;

    public IEnumerable<string> List(string relativeDirectory) =>
        files.Keys
            .Where(p => p.StartsWith(relativeDirectory + "/", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal);

    public string ReadAllText(string relativePath) =>
        files.TryGetValue(relativePath, out var text)
            ? text
            : throw new FileNotFoundException(relativePath);
}
