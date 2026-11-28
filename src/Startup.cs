using System.Globalization;
using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth;

/// <summary>
/// 游戏启动时先做的那几件事：钉住区域设置、建好可写目录、把全部内容文件读一遍、打启动日志。
/// </summary>
/// <remarks>
/// 它登记在工程设置的自动加载里（<c>project.godot</c> 的 <c>[autoload]</c>），所以跑哪个场景都一样 ——
/// 按 F5、按 F6、导出的游戏 —— 它都在场景里的节点之前就绪。这几件事与跑哪个场景无关，所以不挂在某个场景上：
/// 区域设置不钉，数字就照玩家系统的设置印；内容只在用到它的那个场景里读的话，坏掉的那一份要进了那个场景才报错，
/// 导出冒烟判不到。导出冒烟（<c>tools/verify.py</c> 的「跑产物」那一步）认的就是这里打的那几行。
///
/// 读出来的内容这里不留，场景里要用的各自从同一处读（<see cref="ContentBootstrap"/>）；这里读一遍是为了核对。
/// 它没有检查器里的格子：自动加载的脚本节点由引擎建出来，填不了值。要接素材的那几样（鼠标指针、界面）摆在场景里。
/// </remarks>
public partial class Startup : Node
{
    public override void _Ready()
    {
        // 数字照游戏的语言印、不照玩家系统的区域设置印，理由在 GameLanguage。要排在第一行：
        // 下面每一行日志都在格式化数字。
        var systemCulture = CultureInfo.CurrentCulture.Name;
        var culture = GameLanguage.PinCulture();
        GD.Print("[启动] 区域设置 ", culture.Name, "（系统是 ", systemCulture, "，数字照前者印）");

        GD.Print("[启动] 引擎 ", Engine.GetVersionInfo()["string"]);
        GD.Print("[启动] .NET ", System.Environment.Version);

        // 显示指标要晚几帧再打，理由见 _Process。暂停时照样数：场景里一开着界面，世界就停了。
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);

        if (!ModPaths.EnsureWritableDirectories())
        {
            GD.PushError("[启动] 可写目录创建失败，mod 与存档都会不可用");
        }

        GD.Print("[启动] mod 目录 ", ModPaths.ResolveUserPath(ModPaths.ModsRoot));
        GD.Print("[启动] 存档目录 ", ModPaths.ResolveUserPath(ModPaths.SaveRoot));

        var catalog = ContentBootstrap.BuildCatalog();
        GD.Print("[启动] 内容来源 ", string.Join(" → ", catalog.Sources.Select(s => s.Name)));

        var config = ContentBootstrap.LoadConfig(catalog);
        var text = ContentBootstrap.LoadText(catalog);
        var characters = LoadCharacters(catalog);

        // 作物要指到在册的物品，所以物品先读；作物的名字读它产出的那一样物品，文字那一次核对在物品那一步。
        var items = ContentBootstrap.LoadItems(catalog, text);
        var crops = CropDefinition.LoadAll(catalog, items);

        GD.Print("[启动] 名册容量 ", config.RosterCapacity, "（读的是配置文件，不是代码里的常量）");
        GD.Print("[启动] 文本条目 ", text.Count, " 条");
        GD.Print("[启动] 角色定义 ", characters.Count, " 份");
        GD.Print("[启动] 作物定义 ", crops.Count, " 份");
        GD.Print("[启动] 物品定义 ", items.Items.Count, " 份，类别 ", items.Categories.Count, " 个，稀有度 ",
                 items.Rarities.Count, " 档，品质 ", items.Qualities.Count, " 档");

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

    /// <summary>读全部角色定义。两份文件用了同一个标识在这里当场报错，理由见 <see cref="ContentCatalog.ResolveById"/>。</summary>
    private static List<CharacterDefinition> LoadCharacters(ContentCatalog catalog) =>
        [.. catalog.ResolveById(CharacterDefinition.ContentDirectory,
                e => CharacterDefinition.Parse(e.Text, e.RelativePath), c => c.Id).Values];
}
