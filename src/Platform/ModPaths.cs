using Godot;

namespace Tinderhearth.Platform;

/// <summary>
/// 内容与 mod 的加载路径：基础内容在 <c>res://data</c>，mod 在 <c>user://mods/&lt;名字&gt;/</c>，
/// 后者覆盖前者。
/// </summary>
/// <remarks>
/// 顺手纠正一条流传很广的说法：「导出后 <c>res://</c> 只读」是错的。拿导出产物实测过
/// （Windows、非嵌入 pck）：往 <c>res://</c> 写文件会成功，句柄非 null、错误码 Ok，文件落在
/// 可执行文件同级目录里。真正改不了的只是已经打进 <c>.pck</c> 的那些条目。
///
/// 所以 mod 放 <c>user://</c> 的理由不是「res:// 写不了」：
/// <list type="number">
///   <item>exe 同级目录不分用户。多个人共用一份安装时，一个人装的 mod 会影响所有人。</item>
///   <item>真实安装位置常常写不了或会被抹掉：<c>C:\Program Files</c> 下要提权；Steam 库目录
///         即使可写，「验证文件完整性」也会清掉不在清单里的文件，玩家的 mod 会莫名消失。</item>
///   <item>Steam 云存档要求一个按用户区分、位置可预测的目录，<c>user://</c> 正是它。</item>
/// </list>
/// </remarks>
public static class ModPaths
{
    /// <summary>随游戏发布的基础内容，只读。</summary>
    public const string BaseContentRoot = "res://data";

    /// <summary>玩家安装 mod 的位置，可写。每个 mod 一个子目录。</summary>
    public const string ModsRoot = "user://mods";

    /// <summary>存档位置，可写。</summary>
    public const string SaveRoot = "user://saves";

    /// <summary>把 <c>user://</c> 换成本机绝对路径，用来打日志、也用来告诉玩家 mod 该放哪。</summary>
    public static string ResolveUserPath(string godotPath) =>
        ProjectSettings.GlobalizePath(godotPath);

    /// <summary>确保可写目录存在。返回成功与否，失败不抛 —— 调用方要能把这件事说给玩家听。</summary>
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

    /// <summary>列出已安装的 mod 目录名。mod 目录不存在时返回空数组，那不是错误。</summary>
    public static string[] InstalledMods() =>
        DirAccess.DirExistsAbsolute(ModsRoot) ? DirAccess.GetDirectoriesAt(ModsRoot) : [];
}
