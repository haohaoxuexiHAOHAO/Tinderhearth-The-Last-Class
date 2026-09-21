using Godot;
using Tinderhearth.Platform;
using Tinderhearth.UI;
using Tinderhearth.World;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Ui;

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

        // 显示指标延后两帧再打 —— 见 PrintDisplayMetrics 的注释，_Ready 里读到的是中间态。
        SetProcess(true);

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

        _text = text;
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

        BuildUi();
        BuildInputRouter();
        BuildHud();
    }

    private UiRoot _ui = null!;
    private InputRouter _router = null!;
    private LevelHud _hud = null!;
    private WristbandPanel _wristband = null!;
    private TextCatalog _text = null!;
    private IReadOnlyList<PixelTheme.Check> _fontChecks = [];

    /// <summary>
    /// 建关卡 HUD（`UI-8`）。**它不是导航栈里的一层** —— 常驻显示，不压不弹。
    /// </summary>
    /// <remarks>
    /// 数值来自 <see cref="HudDemoModel"/> 那份**明确标为演示**的数据。真数据要等玩法实现，
    /// PRD 第 8 节写明 `UI-1` 不读也不搬 `GP-2` 的参数表。
    ///
    /// 放置是**四角贴边**，从两套候选里选定，几何在
    /// <see cref="HudLayout.AnchorOf"/>。
    /// </remarks>
    private void BuildHud()
    {
        var theme = PixelTheme.Install(out var fontChecks);
        _fontChecks = fontChecks;
        GD.Print("[界面] 像素字体 ", PixelFont.ResourcePath, " ｜ 十项属性核对 ",
                 fontChecks.Count(c => c.Ok), "/", fontChecks.Count, " 一致");

        // 手环也挂上同一份主题。`UI-6` 的注释就写着「像素字体与主题跟着 `UI-8` 落地，
        // 那时把 Theme 挂在根节点上即可，不必改结构」—— 这就是那一步。
        _wristband.Theme = theme;

        _hud = new LevelHud(_router, HudDemoModel.Build(_text, HudLayout.MaxTeammates))
        {
            Name = "LevelHud",
            Theme = theme,
        };
        _ui.LayerOf(UiLayer.Hud).AddChild(_hud);
    }

    /// <summary>建界面根与手环面板（`UI-6`）。</summary>
    /// <remarks>
    /// **这里原先还串着一条启动探针链**（界面骨架自检 → 输入 → 世界空间 UI → 相机 → HUD 排版 →
    /// 相机脚手架），判据打进日志、由 `tools/check_*.py` 读回来判。`ADR-0009` 把开发模式改成编辑器
    /// 主导之后那条链连同它的消费者一起删了：场景与参数归作者在 Godot 里配，画面上的事由他实机看，
    /// 不再用脚本去证明「层级建好了」。**保留的是真正构成游戏的那部分** —— 界面根、手环、HUD。
    /// </remarks>
    private void BuildUi()
    {
        var ui = new UiRoot();
        AddChild(ui);
        _ui = ui;

        var wristband = new WristbandPanel();
        ui.Register(Wristband.Surface, wristband);
        _wristband = wristband;
        ui.Open(Wristband.Surface);

        ui.Context = UiContext.Level;
        wristband.Context = UiContext.Level;
    }

    /// <summary>建输入门面（`UI-7`）。引擎层查询输入一律经它，不直接轮询 <c>Input</c>。</summary>
    private void BuildInputRouter()
    {
        // 不给它注入导航栈：面板打开时世界暂停，玩法节点不在跑，所以门面不需要知道面板开没开。
        _router = new InputRouter { Name = "InputRouter" };
        AddChild(_router);
    }

    private int _framesBeforeMetrics = 2;

    /// <summary>
    /// 等窗口稳定后再打显示指标。**不能在 <c>_Ready</c> 里打。**
    /// </summary>
    /// <remarks>
    /// 实测过这个坑：请求 3840×2160 的窗口时系统会把它裁成 3840×2130，而
    /// <c>_Ready</c> 执行时拉伸还没重算完 —— 那一刻读到的是中间态（逻辑 649×360，与窗口
    /// 尺寸除不通），看起来像配置错了，实际是量早了。两帧之后再读就稳定。
    /// </remarks>
    public override void _Process(double delta)
    {
        if (--_framesBeforeMetrics > 0)
        {
            return;
        }

        SetProcess(false);
        PrintDisplayMetrics();
    }

    /// <summary>
    /// 把显示链路的实际状态打进启动日志（`UI-3`）。
    /// </summary>
    /// <remarks>
    /// 为什么要打而不是写在文档里：像素游戏最贵的一类静默故障就是缩放变成非整数或纹理过滤
    /// 变回线性 —— 画面只是"有点糊"，不报错，可能几个月后才被发现。把逻辑尺寸、窗口尺寸、
    /// 实际缩放倍数与四项设置一起打出来，**换窗口尺寸时扫一眼日志就知道缩放是不是整数**。
    /// 原先有守卫从这份日志里自动判定，它随 `ADR-0009` 删除；日志行还在。缩放倍数取自
    /// <see cref="Viewport.GetFinalTransform"/>，也就是引擎真正用上的那个变换，不是我们以为设了什么。
    ///
    /// **逻辑宽度是下限不是定值。** 实测 `canvas_items` + `expand` + `integer`
    /// 的组合：高度锁在 360，宽度按窗口宽高比撑开（3840×2130 的窗口宽高比 1.803，逻辑尺寸
    /// 就是 649×360），整数缩放取 floor(窗口高 ÷ 360)，除不尽的余量留成黑边。
    /// 所以「满宽 53 个汉字」是地板数而不是定值，**界面必须靠锚点与容器定位** —— 正典那条
    /// 要求不是风格偏好。
    /// </remarks>
    private void PrintDisplayMetrics()
    {
        var logical = GetViewport().GetVisibleRect().Size;
        var window = DisplayServer.WindowGetSize();
        var scale = GetViewport().GetFinalTransform().Scale;

        GD.Print("[显示] 逻辑 ", (int)logical.X, "x", (int)logical.Y,
                 " 窗口 ", window.X, "x", window.Y,
                 " 缩放 x", scale.X.ToString("0.###"), ",", scale.Y.ToString("0.###"));
        GD.Print("[显示] 拉伸 ",
                 ProjectSettings.GetSetting("display/window/stretch/mode"), "/",
                 ProjectSettings.GetSetting("display/window/stretch/aspect"), "/",
                 ProjectSettings.GetSetting("display/window/stretch/scale_mode"),
                 " 纹理过滤 ",
                 ProjectSettings.GetSetting("rendering/textures/canvas_textures/default_texture_filter"));
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
