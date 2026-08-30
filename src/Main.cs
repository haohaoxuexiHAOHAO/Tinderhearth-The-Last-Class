using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth;

/// <summary>
/// 启动场景。**这是 `ENG-2` 的临时脚手架**，不是最终的启动流程。
/// </summary>
/// <remarks>
/// 它现在的职责只有两条：把「内容加载 → 规则层」这条链路真的走通一次，以及给 `ENG-1`
/// 的导出冒烟一个能观察的落点（导出退出码不可信，得看产物真的跑起来并打出东西）。
///
/// 教学关、开局流程与剧情演出都不在这里 —— 那些要等玩法实现需求。
/// </remarks>
public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print("[启动] 引擎 ", Engine.GetVersionInfo()["string"]);
        GD.Print("[启动] .NET ", System.Environment.Version);

        if (!ModPaths.EnsureWritableDirectories())
        {
            GD.PushError("[启动] 可写目录创建失败，mod 与存档都会不可用");
        }

        GD.Print("[启动] mod 目录 ", ModPaths.ResolveUserPath(ModPaths.ModsRoot));
        GD.Print("[启动] 存档目录 ", ModPaths.ResolveUserPath(ModPaths.SaveRoot));

        var catalog = BuildContentCatalog();
        GD.Print("[启动] 内容来源 ", string.Join(" → ", catalog.Sources.Select(s => s.Name)));

        var config = LoadConfig(catalog);
        var text = LoadText(catalog);
        var characters = LoadCharacters(catalog);

        GD.Print("[启动] 名册容量 ", config.RosterCapacity, "（来自配置，非代码常量）");
        GD.Print("[启动] 文本条目 ", text.Count, " 条");
        GD.Print("[启动] 角色定义 ", characters.Count, " 份");

        var roster = new Roster(config.RosterCapacity);
        var controllers = new ActorControllerRegistry();
        foreach (var character in characters)
        {
            roster.TryAdd(character.Id);
            // 谁被玩家驱动由登记表决定，不由「是不是主角」决定（`ENG-5`）。
            controllers.Assign(character.Id, new LocalPlayerController(character.Id));
            GD.Print("[启动]   ", character.Id, " → ", text[character.DisplayNameKey]);
        }

        GD.Print("[启动] ", text["boot.contentReady"], "：在册 ", roster.ActorIds.Count,
                 " 人，控制器 ", controllers.Count, " 个");
    }

    /// <summary>基础内容在前，已安装的 mod 依次叠在后面 —— 后者覆盖前者。</summary>
    private static ContentCatalog BuildContentCatalog()
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new GodotContentSource("base", ModPaths.BaseContentRoot));

        foreach (var mod in ModPaths.InstalledMods())
        {
            catalog.AddSource(new GodotContentSource($"mod:{mod}", $"{ModPaths.ModsRoot}/{mod}"));
        }

        return catalog;
    }

    private static GameConfig LoadConfig(ContentCatalog catalog)
    {
        var entries = catalog.Resolve("config");
        return entries.TryGetValue(GameConfig.ContentPath, out var entry)
            ? GameConfig.Parse(entry.Text)
            : throw new FileNotFoundException($"缺少 {GameConfig.ContentPath}");
    }

    /// <summary>
    /// 文本按**键**合并，不是整文件覆盖 —— 否则 mod 只想改一句台词就会抹掉其余全部文本。
    /// </summary>
    private static TextCatalog LoadText(ContentCatalog catalog)
    {
        // 语言选择归设置系统，本条固定读简体中文，够走通链路。
        const string wanted = "text/zh-CN.json";
        var tables = catalog.ResolveAll("text")
            .Where(e => e.RelativePath == wanted)
            .Select(e => (IReadOnlyDictionary<string, string>)
                ContentJson.Parse<Dictionary<string, string>>(e.Text, $"{e.SourceName}:{wanted}"));

        return TextCatalog.Merge(tables);
    }

    private static List<CharacterDefinition> LoadCharacters(ContentCatalog catalog) =>
        [.. catalog.Resolve(CharacterDefinition.ContentDirectory).Values
                .Select(e => CharacterDefinition.Parse(e.Text, e.RelativePath))];
}
