using Godot;
using Tinderhearth.Platform;
using Tinderhearth.UI;
using Tinderhearth.World;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.UI;

namespace Tinderhearth;

/// <summary>
/// 启动场景。这是临时脚手架，不是最终的启动流程。
/// </summary>
/// <remarks>
/// 它现在只做两件事：把「读内容文件 → 交给规则层」这条链路真的走通一次，以及给导出冒烟一个
/// 能看的落点 —— 导出的退出码不可信，得看产物真的跑起来并打出东西。
///
/// 教学关、开局流程与剧情演出都不在这里，那些要等玩法做起来。
/// </remarks>
public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print("[启动] 引擎 ", Engine.GetVersionInfo()["string"]);
        GD.Print("[启动] .NET ", System.Environment.Version);

        // 显示指标要晚几帧再打：_Ready 这一刻读到的是中间值，理由见 PrintDisplayMetrics。
        SetProcess(true);

        if (!ModPaths.EnsureWritableDirectories())
        {
            GD.PushError("[启动] 可写目录创建失败，mod 与存档都会不可用");
        }

        GD.Print("[启动] mod 目录 ", ModPaths.ResolveUserPath(ModPaths.ModsRoot));
        GD.Print("[启动] 存档目录 ", ModPaths.ResolveUserPath(ModPaths.SaveRoot));

        var catalog = ContentBootstrap.BuildCatalog();
        GD.Print("[启动] 内容来源 ", string.Join(" → ", catalog.Sources.Select(s => s.Name)));

        var config = LoadConfig(catalog);
        var text = LoadText(catalog);
        var characters = LoadCharacters(catalog);

        GD.Print("[启动] 名册容量 ", config.RosterCapacity, "（读的是配置文件，不是代码里的常量）");
        GD.Print("[启动] 文本条目 ", text.Count, " 条");
        GD.Print("[启动] 角色定义 ", characters.Count, " 份");

        _text = text;
        var roster = new Roster(config.RosterCapacity);
        var controllers = new ActorControllerRegistry();
        foreach (var character in characters)
        {
            roster.TryAdd(character.Id);
            // 谁被玩家驱动由这张登记表决定，不由「是不是主角」决定。
            controllers.Assign(character.Id, new LocalPlayerController(character.Id));
            GD.Print("[启动]   ", character.Id, " → ", text[character.DisplayNameKey]);
        }

        GD.Print("[启动] ", text["boot.contentReady"], "：在册 ", roster.ActorIds.Count,
                 " 人，控制器 ", controllers.Count, " 个");

        BuildUI();
        BuildInputRouter();
        BuildHud();
    }

    private UIRoot _ui = null!;
    private InputRouter _router = null!;
    private LevelHud _hud = null!;
    private WristbandPanel _wristband = null!;
    private TextCatalog _text = null!;
    private IReadOnlyList<PixelTheme.Check> _fontChecks = [];

    /// <summary>
    /// 建关卡 HUD。它不进导航栈 —— 常驻显示，不压栈也不弹栈。
    /// </summary>
    /// <remarks>
    /// 显示的数值来自 <see cref="HudDemoModel"/>，那是一份明确标为演示用的数据，真数据要等玩法
    /// 做起来。四块各贴一个屏幕角，具体贴哪个角见 <see cref="HudLayout.AnchorOf"/>。
    /// </remarks>
    private void BuildHud()
    {
        var theme = PixelTheme.Install(out var fontChecks);
        _fontChecks = fontChecks;
        GD.Print("[界面] 像素字体 ", PixelFont.ResourcePath, " ｜ 导入属性核对 ",
                 fontChecks.Count(c => c.Ok), "/", fontChecks.Count, " 一致");

        // 手环也挂同一份主题。它自己不载字体，等的就是这一步。
        _wristband.Theme = theme;

        _hud = new LevelHud(_router, HudDemoModel.Build(_text, HudLayout.MaxTeammates))
        {
            Name = "LevelHud",
            Theme = theme,
        };
        _ui.LayerOf(UILayer.Hud).AddChild(_hud);
    }

    /// <summary>建界面根节点与手环面板。</summary>
    /// <remarks>
    /// 这里只建真正构成游戏的那几样：界面根、手环、HUD。画面上的事不用脚本去证明，
    /// 由作者在 Godot 里实机看。
    /// </remarks>
    private void BuildUI()
    {
        var ui = new UIRoot();
        AddChild(ui);
        _ui = ui;

        var wristband = new WristbandPanel();
        ui.Register(Wristband.Surface, wristband);
        _wristband = wristband;
        ui.Open(Wristband.Surface);

        ui.Context = UIContext.Level;
        wristband.Context = UIContext.Level;
    }

    /// <summary>建输入门面。引擎层查输入一律经它，不直接轮询 <c>Input</c>。</summary>
    private void BuildInputRouter()
    {
        // 不把导航栈传给它：面板打开时世界暂停、玩法节点不在跑，所以门面不需要知道面板开没开。
        _router = new InputRouter { Name = "InputRouter" };
        AddChild(_router);
    }

    private int _framesBeforeMetrics = 2;

    /// <summary>
    /// 等窗口稳定下来再打显示指标。不能在 <c>_Ready</c> 里打。
    /// </summary>
    /// <remarks>
    /// 实测踩过：请求 3840×2160 的窗口时系统把它裁成 3840×2130，而 <c>_Ready</c> 执行时拉伸还
    /// 没重算完 —— 那一刻读到的是中间值（逻辑 649×360，拿窗口尺寸去除除不通），看起来像配置
    /// 错了，其实是量早了。等几帧再读就稳定。
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
    /// 把显示链路的实际状态打进启动日志。
    /// </summary>
    /// <remarks>
    /// 打出来而不是写在文档里，是因为像素游戏最难发现的一类毛病就是缩放变成非整数、或者纹理过滤
    /// 退回线性 —— 画面只是有点糊，不报错，可能几个月后才被注意到。换窗口尺寸之后扫一眼日志就
    /// 知道缩放是不是整数。缩放倍数取自 <see cref="Viewport.GetFinalTransform"/>，是引擎真正用
    /// 上的那个变换，不是我们以为自己设了什么。
    ///
    /// 逻辑宽度是下限、不是定值。实测当前这套拉伸设置：高度锁死，宽度按窗口宽高比撑开
    /// （3840×2130 的窗口得到逻辑 649×360），整数缩放取窗口高除以逻辑高之后向下取整，除不尽的
    /// 余量留成黑边。所以一行能排多少个汉字也是个下限，界面必须靠锚点与容器定位。
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

    private static GameConfig LoadConfig(ContentCatalog catalog)
    {
        var entries = catalog.Resolve("config");
        return entries.TryGetValue(GameConfig.ContentPath, out var entry)
            ? GameConfig.Parse(entry.Text)
            : throw new FileNotFoundException($"缺少 {GameConfig.ContentPath}");
    }

    /// <summary>
    /// 文本一条一条按键合并，不是整个文件覆盖 —— 否则 mod 只想改一句台词就会抹掉其余全部文本。
    /// </summary>
    private static TextCatalog LoadText(ContentCatalog catalog)
    {
        // 选哪种语言属于玩家自己的偏好，它跨存档位、不进任何存档分片，见设计仓
        // design/存档系统.md 的「玩家级偏好：第三样东西，不是分片」一节。这里先固定读简体中文。
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
