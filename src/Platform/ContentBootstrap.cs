using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Platform;

/// <summary>把内容来源按顺序叠成一份目录：基础内容在前，已安装的 mod 依次叠在后面。</summary>
/// <remarks>
/// **它存在的理由是这段装配有两个以上的调用方。** 启动流程要它，而单独跑一个场景（按 <c>F6</c>
/// 试一片地那种）时那个场景也要它 —— 两处各写一份的话，往里加一个内容来源时漏改哪一处不报错，
/// 表现只是「mod 在某些场景里不生效」。
///
/// 顺序就是覆盖顺序，语义与两种覆盖形状的区别都在 <see cref="ContentCatalog"/>，本类不复述。
/// </remarks>
public static class ContentBootstrap
{
    /// <summary>建一份内容目录。基础内容在前，已安装的 mod 依次叠在后面 —— 后者覆盖前者。</summary>
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
