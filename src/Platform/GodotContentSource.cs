using Godot;
using Tinderhearth.Rules.Foundation.Content;

// ImplicitUsings 会引入 System.IO，于是 FileAccess 这个名字在 Godot.FileAccess 与
// System.IO.FileAccess 之间有歧义（CS0104）。取别名而不是关掉 ImplicitUsings：
// 歧义只在真正用到它的文件里解决一次，读代码的人也看得出用的是哪一个。
using FileAccess = Godot.FileAccess;

namespace Tinderhearth.Platform;

/// <summary>
/// 用 Godot 的 <see cref="FileAccess"/>／<see cref="DirAccess"/> 读内容文件。
/// </summary>
/// <remarks>
/// 不能用普通 .NET 的 <c>System.IO.File</c>：导出之后 <c>res://</c> 的内容打进 <c>.pck</c> 包里，
/// 只有 Godot 自己的 <c>FileAccess</c> 读得到。<c>user://</c> 是真实目录、两种读法都行，但统一走
/// <c>FileAccess</c>，免得两种路径两种行为。
/// </remarks>
public sealed class GodotContentSource : IContentSource
{
    private readonly string _root;

    /// <param name="name">来源名。它会出现在日志里，也会出现在「因为缺某个 mod 而不可用」的提示里。</param>
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
            // 只认 .json 原名。目录里还会有引擎自己生成的附属文件，那些不是内容。
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
