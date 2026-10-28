namespace Tinderhearth.Rules.Tests;

/// <summary>读仓库里那几份文件的测试共用的一处：找仓库根。</summary>
/// <remarks>
/// 测试只读不写。仓库根从测试程序所在目录往上找 <c>project.godot</c>，找不到就抛 —— 不当成「那份文件是空的」，
/// 否则读不到文件的那一轮看起来像通过。
/// </remarks>
internal static class RepoFiles
{
    /// <summary>仓库根的绝对路径。</summary>
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 project.godot");
    }
}
