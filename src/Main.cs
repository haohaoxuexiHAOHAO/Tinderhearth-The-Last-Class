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

        ProbeUiSkeleton();
        ProbeInputMapping();
        BuildHud();
        ProbeCamera(config);
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
    /// 放置是**四角贴边**，由作者 2026-08-31 从两套候选里选定，几何在
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

    /// <summary>
    /// 让 `UI-5` 的相机行为在启动时真跑一遍并打出判据。
    /// </summary>
    /// <remarks>
    /// **同样是脚手架**，`UI-10` 的端到端测试会替换它。理由与另两个 Probe 一致：规则层测试证明
    /// 得了死区与钳制这些纯几何，证明不了「<c>Camera2D</c> 的 <c>Zoom</c> 真是那个整数」，更证明
    /// 不了「分数像素位移会不会破坏像素对齐」—— 后者要截图量像素块。判据打进日志，
    /// `tools/check_camera.py` 读回来判。
    ///
    /// 可建造区尺寸从这里的 <paramref name="config"/> 进相机，**代码里不出现 40 与 30**
    /// （PRD 的 `FR-24`）。
    ///
    /// 自检跑完才建 <see cref="CameraHarness"/>：探针为了两种视角各测一遍会反复建与释放相机，
    /// 而脚手架要的是一台长期活着的。同时存在会抢 <c>current</c>，那种依赖时序的正确性正是
    /// 「实机上偶尔不对」的来源。
    /// </remarks>
    private void ProbeCamera(GameConfig config)
    {
        var probe = new CameraProbe(config) { Name = "CameraProbe" };
        probe.Finished += () => ProbeHud(config);
        AddChild(probe);
    }

    /// <summary>
    /// 让 `UI-8` 的 HUD 排版在启动时真量一遍并打出判据。
    /// </summary>
    /// <remarks>
    /// **排在相机自检之后、脚手架之前**，这个顺序是必须的：本探针会改窗口尺寸（撑开逻辑宽度才
    /// 量得出锚点有没有生效），而相机自检那一段要在固定窗口下逐像素比对截图，脚手架的存图也要在
    /// 标准窗口尺寸下取。三者抢同一个窗口，只能串起来。
    /// </remarks>
    private void ProbeHud(GameConfig config)
    {
        var probe = new HudProbe(_hud, _router, _text, _fontChecks) { Name = "HudProbe" };
        probe.Finished += () => BuildCameraHarness(config);
        AddChild(probe);
    }

    /// <summary>
    /// 建相机的验收脚手架（`UI-5` 的实机确认 + `UI-12` 的手感校准工具）。
    /// </summary>
    /// <remarks>
    /// **同样是脚手架**，`UI-8` 的 HUD 加在它上面，`UI-10` 的端到端测试会替换它。它存在的理由
    /// 很直接：`UI-5` 的机器判据全绿，但「作者实机确认」那条没有执行它的办法 —— 场景里没有地面
    /// 也没有能走的角色，死区取多大、震动会不会晕都无从判起。
    ///
    /// `--headless` 下不建：那时没有窗口可看，而它会往场景里塞上百个节点，白占跑产物那一步的
    /// 时间。判据与 <see cref="CameraProbe"/> 里那条一致，取 <c>DisplayServer.GetName()</c>。
    /// </remarks>
    private void BuildCameraHarness(GameConfig config)
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[脚手架] 跳过相机验收场景 —— 显示后端 headless，没有窗口可看");
            return;
        }

        AddChild(new CameraHarness(config, _ui, _router, _hud,
                                   (mates, objective) => HudDemoModel.Build(_text, mates, objective))
        {
            Name = "CameraHarness",
        });
    }

    /// <summary>
    /// 让 `UI-7` 的输入链路在启动时真跑一遍并打出判据。
    /// </summary>
    /// <remarks>
    /// **同样是脚手架**，`UI-10` 的端到端测试会替换它。理由与 `ProbeUiSkeleton` 一致：规则层测试
    /// 证明不了「符号翻译成了对的引擎枚举」与「组合真的经过引擎的事件流」，那两件事要有引擎在场，
    /// 而引擎内测试底座还不存在（`ENG-6`）。判据打进日志，`tools/check_input_map.py` 读回来判。
    /// </remarks>
    private void ProbeInputMapping()
    {
        _router = new InputRouter { Name = "InputRouter" };
        AddChild(_router);

        _router.DeviceChanged += device => GD.Print("[输入] 设备切换 → ", device);
        _router.SkillGroupChanged += group => GD.Print("[输入] 技能组切换 → ", group);

        AddChild(new InputProbe(_router) { Name = "InputProbe" });
    }

    /// <summary>
    /// 让 `UI-6` 的界面骨架在启动时真跑一遍并打出状态。
    /// </summary>
    /// <remarks>
    /// **这是脚手架的一部分，`UI-8` 会用真正的 HUD 样板场景替换它。** 放在这里的理由与
    /// `[显示]` 那几行一样：「层级建好了」「关卡内禁用了管理页」如果只写在文档里，就没有任何
    /// 东西能证明它此刻仍然成立。跑一次并打出来，验收入口读日志就能判。
    /// </remarks>
    private void ProbeUiSkeleton()
    {
        var ui = new UiRoot();
        AddChild(ui);
        _ui = ui;

        var wristband = new WristbandPanel();
        ui.Register(Wristband.Surface, wristband);
        _wristband = wristband;
        ui.Open(Wristband.Surface);

        var 派工 = new UiSurface("roster", UiLayer.Panel, PausesWorld: false, SurfaceKind.Manage);
        ui.Register(派工, new Control());

        ui.Context = UiContext.Base;
        wristband.Context = UiContext.Base;
        var 基地可用 = ui.Open(派工);
        ui.Close(派工);

        ui.Context = UiContext.Level;
        wristband.Context = UiContext.Level;
        var 关卡可用 = ui.Open(派工);

        GD.Print("[界面] 管理类面板：基地内可开 ", 基地可用, "，关卡内可开 ", 关卡可用);
        GD.Print("[界面] 手环标签页 关卡内可用 ",
                 string.Join("、", Wristband.AvailableIn(UiContext.Level).Select(t => t.Id)),
                 "；被禁用 ",
                 string.Join("、", Wristband.Tabs
                     .Where(t => !t.AvailableIn(UiContext.Level)).Select(t => t.Id)));
        GD.Print("[界面] 导航栈深度 ", ui.Navigation.Depth,
                 "，世界该暂停 ", ui.Navigation.WorldShouldPause);
    }

    private int _framesBeforeMetrics = 2;

    /// <summary>
    /// 等窗口稳定后再打显示指标。**不能在 <c>_Ready</c> 里打。**
    /// </summary>
    /// <remarks>
    /// 2026-08-30 实测过这个坑：请求 3840×2160 的窗口时系统会把它裁成 3840×2130，而
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
    /// 实际缩放倍数与四项设置一起打出来，`tools/check_scaling.py` 就能从日志里判定，而不必
    /// 靠人盯着看。缩放倍数取自 <see cref="Viewport.GetFinalTransform"/>，也就是引擎真正
    /// 用上的那个变换，不是我们以为自己设了什么。
    ///
    /// **逻辑宽度是下限不是定值。** 2026-08-30 实测 `canvas_items` + `expand` + `integer`
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
