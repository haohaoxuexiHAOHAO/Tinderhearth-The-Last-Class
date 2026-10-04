using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Platform;

/// <summary>把内容来源按顺序叠成一份目录：基础内容在前，已安装的 mod 依次叠在后面。</summary>
/// <remarks>
/// 单独抽出来是因为不止一处要用：启动流程要它，在编辑器里单独跑一个场景时那个场景也要它。
/// 两处各写一份的话，往里加一个内容来源时漏改哪一处都不报错，表现只是「mod 在某些场景里不生效」。
///
/// 这里的先后顺序就是覆盖顺序；覆盖怎么算见 <see cref="ContentCatalog"/>，本类不重复一遍。
/// </remarks>
public static class ContentBootstrap
{
    /// <summary>建一份内容目录。基础内容在前，已安装的 mod 依次叠在后面，后面的覆盖前面的。</summary>
    public static ContentCatalog BuildCatalog()
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new GodotContentSource("base", ModPaths.BaseContentRoot));

        foreach (var mod in ModPaths.InstalledMods())
        {
            catalog.AddSource(new GodotContentSource($"mod:{mod}", $"{ModPaths.ModsRoot}/{mod}"));
        }

        return catalog;
    }
}
