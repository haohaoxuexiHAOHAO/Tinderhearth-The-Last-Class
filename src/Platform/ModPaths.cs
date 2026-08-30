using Godot;

namespace Tinderhearth.Platform;

/// <summary>
/// 内容与 mod 的加载路径（`ENG-2` 的决定项）。
/// </summary>
/// <remarks>
/// **基础内容在 <c>res://data</c>，mod 在 <c>user://mods/&lt;名字&gt;/</c>，后者覆盖前者。**
///
/// 为什么 mod 不能放 <c>res://</c>：<c>res://</c> 在导出后是打进 <c>.pck</c> 的只读内容，
/// 玩家装 mod 需要一个**运行时可写**的位置，而 <c>user://</c> 就是 Godot 为此提供的
/// 按平台分配的可写目录（Windows 上在 <c>%APPDATA%</c> 下）。这同时满足 Steam 云存档
/// 「存档要落在可预测位置」的要求（`ENG-8`）。
///
/// 「导出后 <c>res://</c> 只读」这条**本条只验证了 <c>user://</c> 可写这一半**：确认
/// <c>res://</c> 在导出产物里不可写需要一个真的导出包，而那是 `ENG-1` 的事，已在那里记账。
/// </remarks>
public static class ModPaths
{
    /// <summary>随游戏发布的基础内容，只读。</summary>
    public const string BaseContentRoot = "res://data";

    /// <summary>玩家安装 mod 的位置，可写。每个 mod 一个子目录。</summary>
    public const string ModsRoot = "user://mods";

    /// <summary>存档位置，可写。</summary>
    public const string SaveRoot = "user://saves";

    /// <summary>把 <c>user://</c> 解析成本机绝对路径，用于诊断输出与向玩家说明 mod 放哪。</summary>
    public static string ResolveUserPath(string godotPath) =>
        ProjectSettings.GlobalizePath(godotPath);

    /// <summary>确保可写目录存在。返回是否成功，失败不抛 —— 调用方要能把它报给玩家。</summary>
    public static bool EnsureWritableDirectories()
    {
        foreach (var path in new[] { ModsRoot, SaveRoot })
        {
            if (DirAccess.DirExistsAbsolute(path))
            {
                continue;
            }

            if (DirAccess.MakeDirRecursiveAbsolute(path) != Error.Ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>列出已安装的 mod 目录名。目录不存在时返回空数组，不是错误。</summary>
    public static string[] InstalledMods() =>
        DirAccess.DirExistsAbsolute(ModsRoot) ? DirAccess.GetDirectoriesAt(ModsRoot) : [];
}
