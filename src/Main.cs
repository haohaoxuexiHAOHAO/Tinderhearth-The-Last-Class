using System.Globalization;
using Godot;
using Tinderhearth.Platform;
using Tinderhearth.UI;
using Tinderhearth.World;
using Tinderhearth.Rules.Economy;
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
    /// <summary>素材资源。界面件一律从它取。</summary>
    /// <remarks>
    /// 主场景的节点树是代码搭的，所以这一格由代码那一侧在 <c>scenes/main.tscn</c> 里填好，接的是
    /// <c>assets/self-drawn/ui/ui_art.tres</c>；缺了、或那一份缺格，启动时当场报错并一次列全。
    /// </remarks>
    [Export]
    public UIArt? Art { get; set; }

    /// <summary>主场景预览哪个场合的 HUD。</summary>
    /// <remarks>
    /// 不直接导出 <see cref="UIContext"/>：它的第一个取值是基地，导出它就等于留了一个能用的默认值，没选也不报错。
    /// 这里第一个取值是「没选」，没选就当场报错。
    /// </remarks>
    public enum HudPreview
    {
        /// <summary>没选。启动时当场报错，不悄悄挑一个。</summary>
        Unset,

        /// <summary>关卡那一套：资源、任务目标、队友与技能栏，全部在顶上。</summary>
        Level,

        /// <summary>基地那一套：精力与状态位在左下，随身栏在右下，带演示内容。</summary>
        Base,
    }

    /// <summary>预览哪一套 HUD，在检查器里选。手环按同一个场合判哪些页可用。</summary>
    [Export]
    public HudPreview Preview { get; set; }

    /// <summary>资源条与精力管怎么动：回复时追多快、扣减那一截停多久、中段流多快、气泡浮多快。</summary>
    /// <remarks>
    /// 那一份就放在 <c>scenes/main.tscn</c> 里这一格上，作者在检查器里展开它调；没接、或哪个量没填，启动时当场报错。
    /// </remarks>
    [Export]
    public GaugeMotion? Gauges { get; set; }

    public override void _Ready()
    {
        // 数字照游戏的语言印、不照玩家系统的区域设置印，理由在 GameLanguage。要排在第一行：
        // 下面每一行日志都在格式化数字。
        var systemCulture = CultureInfo.CurrentCulture.Name;
        var culture = GameLanguage.PinCulture();
        GD.Print("[启动] 区域设置 ", culture.Name, "（系统是 ", systemCulture, "，数字照前者印）");

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
        var text = ContentBootstrap.LoadText(catalog);
        var characters = LoadCharacters(catalog);

        // 主场景用不上物品与作物，读它们只为这一次核对：农场不是主场景，只在农场里读的话，坏掉的那一份要进了农场才报错，
        // 导出冒烟判不到。作物要指到在册的物品，所以物品先读；作物的名字读它产出的那一样物品，文字那一次核对在物品那一步。
        var items = ContentBootstrap.LoadItems(catalog, text);
        var crops = CropDefinition.LoadAll(catalog, items);

        GD.Print("[启动] 名册容量 ", config.RosterCapacity, "（读的是配置文件，不是代码里的常量）");
        GD.Print("[启动] 文本条目 ", text.Count, " 条");
        GD.Print("[启动] 角色定义 ", characters.Count, " 份");
        GD.Print("[启动] 作物定义 ", crops.Count, " 份");
        GD.Print("[启动] 物品定义 ", items.Items.Count, " 份，类别 ", items.Categories.Count, " 个，稀有度 ",
                 items.Rarities.Count, " 档，品质 ", items.Qualities.Count, " 档");

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

        // 素材资源在搭界面之前核：缺一张图是这里一条说清缺哪几格的报错，不是画面上悄悄空一块。
        _art = UIArtGuard.Require(Art, $"{nameof(Main)}（节点 {Name}）");
        GD.Print("[界面] 素材资源 ", UIArtManifest.Pieces.Count, " 格，全部在、尺寸都对（", _art.ResourcePath, "）");

        _gauges = GaugeMotion.Require(Gauges, $"{nameof(Main)}（节点 {Name}）");
        GD.Print("[界面] 资源条 追 ", _gauges.Chase.PixelsPerSecond, " 像素每秒，扣减那一截停 ", _gauges.Chase.HoldSeconds,
                 " 秒，中段流 ", _gauges.FlowPixelsPerSecond, " 像素每秒，精力管气泡浮 ", _gauges.BubblePixelsPerSecond, " 像素每秒");

        _context = Preview switch
        {
            HudPreview.Level => UIContext.Level,
            HudPreview.Base => UIContext.Base,
            _ => throw new InvalidOperationException(
                $"{nameof(Main)}（节点 {Name}）的 {nameof(Preview)} 没选 —— 在检查器里选预览关卡还是基地那一套 HUD"),
        };
        GD.Print("[界面] 预览场合 ", _context);

        BuildUI();
        BuildInputRouter();
        BuildHud();
    }

    private UIArt _art = null!;
    private GaugeMotionSettings _gauges = null!;
    private UIContext _context;
    private UIRoot _ui = null!;
    private InputRouter _router = null!;
    private WristbandPanel _wristband = null!;
    private TextCatalog _text = null!;
    private IReadOnlyList<PixelTheme.Check> _fontChecks = [];

    /// <summary>
    /// 建 <see cref="Preview"/> 选的那一套 HUD。它不进导航栈 —— 常驻显示，不压栈也不弹栈。
    /// </summary>
    /// <remarks>
    /// 显示的数值来自 <see cref="HudDemoModel"/>，那是一份明确标为演示用的数据，真数据要等玩法
    /// 做起来。各块贴哪见 <see cref="HudLayout.PlacementOf"/>。基地那一套的随身栏在正式场景里由作者摆，
    /// 这里的场景树是代码搭的，所以由这里建一条、装上演示内容。
    /// </remarks>
    private void BuildHud()
    {
        var theme = PixelTheme.Install(_art, out var fontChecks);
        _fontChecks = fontChecks;
        GD.Print("[界面] 像素字体 ", PixelFont.ResourcePath, " ｜ 导入属性核对 ",
                 fontChecks.Count(c => c.Ok), "/", fontChecks.Count, " 一致");

        // 手环也挂同一份主题。它自己不载字体，等的就是这一步。
        _wristband.Theme = theme;

        var layer = _ui.LayerOf(UILayer.Hud);
        switch (_context)
        {
            case UIContext.Level:
                var start = HudDemoModel.Build(_text, HudLayout.MaxTeammates);
                var hud = new LevelHud(_router, start, _art, HudDemoModel.SkillIcon, _gauges)
                {
                    Name = "LevelHud",
                    Theme = theme,
                };
                layer.AddChild(hud);

                // 演示数据按一轮起伏走，好在实机看资源条怎么追、怎么留一截。
                AddChild(new HudDemoClock(hud, start));
                break;

            case UIContext.Base:
                layer.AddChild(new BaseHud(HudDemoModel.BuildBase(_text), _art, _gauges)
                {
                    Name = "BaseHud",
                    Theme = theme,
                });

                // 内容在挂进树之前装好：就绪那一刻就按它画，图标从哪取也得在那之前给。
                var carry = new CarrySlotBarView
                {
                    Name = "CarrySlotBar",
                    Router = _router,
                    Art = _art,
                    Icons = HudDemoModel.CarryIcon,
                    Theme = theme,
                };
                carry.Bar.Fill(HudDemoModel.CarrySlots());
                layer.AddChild(carry);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(_context), $"没有这个场合：{_context}");
        }
    }

    /// <summary>建界面根节点、手环面板与鼠标指针。</summary>
    /// <remarks>
    /// 这里只建真正构成游戏的那几样：界面根、手环、HUD、鼠标指针。画面上的事不用脚本去证明，
    /// 由作者在 Godot 里实机看。
    /// </remarks>
    private void BuildUI()
    {
        AddChild(new CursorSkin { Name = "CursorSkin", Art = _art });

        var ui = new UIRoot();
        AddChild(ui);
        _ui = ui;

        var wristband = new WristbandPanel { Art = _art, Catalog = _text };
        ui.Register(Wristband.Surface, wristband);
        _wristband = wristband;
        ui.Open(Wristband.Surface);

        ui.Context = _context;
        wristband.Context = _context;
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

    /// <summary>读全部角色定义。两份文件用了同一个标识在这里当场报错，理由见 <see cref="ContentCatalog.ResolveById"/>。</summary>
    private static List<CharacterDefinition> LoadCharacters(ContentCatalog catalog) =>
        [.. catalog.ResolveById(CharacterDefinition.ContentDirectory,
                e => CharacterDefinition.Parse(e.Text, e.RelativePath), c => c.Id).Values];
}
