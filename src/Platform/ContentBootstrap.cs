using Godot;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;

// FileAccess 在 Godot.FileAccess 与 System.IO.FileAccess 之间有歧义（CS0104），理由与取别名的做法同 GodotContentSource。
using FileAccess = Godot.FileAccess;

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

    /// <summary>读发行包里那份色板。读不到就抛：物品档位的显示色按它核，少了它那几条就没有执行。</summary>
    /// <remarks>
    /// 色板不是引擎资源，导出时靠代码仓 <c>export_presets.cfg</c> 的 include_filter 点名才进包；漏了的表现是这里抛，
    /// 而 <c>tools/verify.py</c> 跑产物那一步判得到（「物品定义」那一行打不出来）。mod 不提供色板：它是美术的，不是内容。
    /// </remarks>
    public static Palette LoadPalette()
    {
        using var file = FileAccess.Open(Palette.ResourcePath, FileAccess.ModeFlags.Read);
        return file is not null
            ? Palette.Parse(file.GetAsText().Split('\n'), Palette.ResourcePath)
            : throw new FileNotFoundException(
                $"读不到色板 {Palette.ResourcePath}（Godot 错误 {FileAccess.GetOpenError()}）—— "
                + "导出时它要在 export_presets.cfg 的 include_filter 里");
    }

    /// <summary>读物品、类别与两条轴的档位，再核文本表：坏一份当场报错；文本表里缺的，打一条错误、游戏照跑。</summary>
    /// <remarks>
    /// 放在这里、不写在主场景里：农场不经过主场景，用到物品时要从同一处读，理由同 <see cref="BuildCatalog"/>。
    /// 缺文字不停下来，与 <see cref="TextCatalog"/> 缺键显成占位、不抛是同一个理由。
    /// </remarks>
    public static ItemCatalog LoadItems(ContentCatalog catalog, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var items = ItemCatalog.Load(catalog, LoadPalette().BandOf);
        var missing = text.MissingKeys(items.TextKeys);
        if (missing.Count > 0)
        {
            GD.PushError($"[启动] 文本表 {GameLanguage.TextTablePath} 里缺 {missing.Count} 条物品与档位的文字，"
                         + $"界面上会显成缺文本的占位：{string.Join("、", missing)}");
        }

        return items;
    }
}
