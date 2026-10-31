using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;

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

    /// <summary>读这种语言的文本表：各来源里那一份逐条按键合并，后面的覆盖前面的同键条目。</summary>
    /// <remarks>
    /// 一条一条按键合并，不是整个文件覆盖：否则 mod 只想改一句台词就会抹掉其余全部文本。
    /// 主场景与在编辑器里单独跑的农场都要它，理由同 <see cref="BuildCatalog"/>。
    /// </remarks>
    public static TextCatalog LoadText(ContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        // 读哪种语言的文本表由 GameLanguage 定，以后改成读玩家级偏好也在那里改。
        var wanted = GameLanguage.TextTablePath;
        var tables = catalog.ResolveAll("text")
            .Where(e => e.RelativePath == wanted)
            .Select(e => (IReadOnlyDictionary<string, string>)
                ContentJson.Parse<Dictionary<string, string>>(e.Text, $"{e.SourceName}:{wanted}"));

        return TextCatalog.Merge(tables);
    }
}
