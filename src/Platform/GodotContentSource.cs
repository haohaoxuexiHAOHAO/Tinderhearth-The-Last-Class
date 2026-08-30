using Godot;
using Tinderhearth.Rules.Foundation.Content;

// `ImplicitUsings` 会引入 System.IO，于是 `FileAccess` 在 Godot.FileAccess 与
// System.IO.FileAccess 之间歧义（CS0104）。显式取别名而不是关掉 ImplicitUsings ——
// 这样歧义只在真正用到它的文件里解决一次，读代码的人也能看出用的是哪一个。
using FileAccess = Godot.FileAccess;

namespace Tinderhearth.Platform;

/// <summary>
/// 用 Godot 的 <see cref="FileAccess"/>／<see cref="DirAccess"/> 读内容文件。
/// </summary>
/// <remarks>
/// 为什么不能用普通 .NET 的 <c>System.IO.File</c>：导出后 <c>res://</c> 的内容打进
/// <c>.pck</c> 包里，只有 Godot 自己的 <c>FileAccess</c> 能读。<c>user://</c> 是真实
/// 目录，两种读法都行，但统一走 <c>FileAccess</c> 免得两套路径两种行为。
/// </remarks>
public sealed class GodotContentSource : IContentSource
{
    private readonly string _root;

    /// <param name="name">来源名，会出现在诊断与「因缺少 mod 而不可用」的提示里。</param>
    /// <param name="root">Godot 路径前缀，例如 <c>res://data</c> 或 <c>user://mods/foo</c>。</param>
    public GodotContentSource(string name, string root)
    {
        Name = name;
        _root = root.TrimEnd('/');
    }

    public string Name { get; }

    public IEnumerable<string> List(string relativeDirectory)
    {
        var absolute = $"{_root}/{relativeDirectory}";
        if (!DirAccess.DirExistsAbsolute(absolute))
        {
            // 目录不存在是正常情况：一个 mod 完全可以只提供角色、不提供文本。
            yield break;
        }

        foreach (var file in DirAccess.GetFilesAt(absolute))
        {
            // 导出后 .json 之类的非资源文件可能被改名为 .import 附属物，这里只认原名。
            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{relativeDirectory}/{file}";
            }
        }
    }

    public string ReadAllText(string relativePath)
    {
        var absolute = $"{_root}/{relativePath}";
        using var handle = FileAccess.Open(absolute, FileAccess.ModeFlags.Read);
        if (handle is null)
        {
            throw new FileNotFoundException(
                $"读不到内容文件：{absolute}（Godot 错误 {FileAccess.GetOpenError()}）");
        }

        return handle.GetAsText();
    }
}
