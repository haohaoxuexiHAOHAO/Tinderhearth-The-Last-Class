using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 界面根节点：按固定层级建好每层画布，管导航栈、返回键与暂停。画布层只在这里建一次。
/// </summary>
/// <remarks>
/// 规则都在规则层：按返回键回到哪、关卡内哪些页能用、打开这一层要不要暂停，都由
/// <see cref="NavigationStack"/> 与 <see cref="UISurface"/> 判，本类只负责把结论翻译成节点的
/// 显隐与 <c>SceneTree.Paused</c>。
///
/// 不让各场景自己摆 CanvasLayer：那样每个界面会自己挑一个层号，迟早出现「弹窗被 HUD 挡住」，
/// 而排查时得翻遍所有场景才知道谁用了哪个号。
///
/// <see cref="UILayer.WorldSpace"/> 那一层开了 <c>FollowViewportEnabled</c>，因为挂在它上面的
/// 东西（读条这类）要画在执行者身上、跟着相机走并随缩放变化。
/// </remarks>
public partial class UIRoot : Node
{
    private readonly Dictionary<UILayer, CanvasLayer> _layers = [];
    private readonly Dictionary<string, Control> _surfaces = [];
    private readonly NavigationStack _nav = new();

    /// <summary>当前场合（基地还是关卡里）。切场景时由场景设置，决定手环哪些页可用。</summary>
    public UIContext Context { get; set; } = UIContext.Base;

    /// <summary>导航栈。只给需要查询的地方读，压栈与弹栈一律走本类的方法。</summary>
    public NavigationStack Navigation => _nav;

    public override void _Ready()
    {
        foreach (UILayer layer in Enum.GetValues<UILayer>())
        {
            if (layer == UILayer.World)
            {
                continue;   // 世界不是 UI 层，它就是场景本身
            }

            var canvas = new CanvasLayer
            {
                Name = layer.ToString(),
                Layer = (int)layer,
                // 世界空间那一层跟随相机；其余是屏幕空间，固定不动。
                FollowViewportEnabled = layer == UILayer.WorldSpace,
            };
            AddChild(canvas);
            _layers[layer] = canvas;
        }

        GD.Print("[界面] 层级 ", string.Join(" → ",
            Enum.GetValues<UILayer>().Select(l => $"{l}({(int)l})")));
        GD.Print("[界面] 排版单位 栅格 ", UIMetrics.Grid,
                 "｜内边距 ", UIMetrics.PanelPadding,
                 "｜间距 ", UIMetrics.ItemGap,
                 "｜安全边距 ", UIMetrics.SafeMargin,
                 "｜图标 ", UIMetrics.IconSmall, "/", UIMetrics.IconLarge,
                 "｜满宽汉字下限 ", UIMetrics.MaxFullWidthChars);
    }

    /// <summary>取某一层的画布，用来往上挂节点。</summary>
    public CanvasLayer LayerOf(UILayer layer) =>
        _layers.TryGetValue(layer, out var canvas)
            ? canvas
            : throw new ArgumentOutOfRangeException(nameof(layer), $"没有这一层：{layer}");

    /// <summary>
    /// 登记一个界面：把它挂到自己声明的层上，初始隐藏。
    /// </summary>
    /// <remarks>
    /// 布局一律靠锚点与容器：这里强制铺满所属层，具体位置由界面内部的容器决定。不写死绝对像素
    /// 坐标，因为逻辑宽度会变 —— 实测 3840×2130 的窗口得到逻辑 649×360，写死坐标的界面在宽
    /// 窗口上会错位。
    ///
    /// 铺满用 <c>SetAnchorsAndOffsetsPreset</c> 而不是 <c>SetAnchorsPreset</c>：实测对已经在树里
    /// 的节点调后者，引擎会把偏移改写成负的视口尺寸去保住当前那个 0×0 矩形，锚点对了尺寸还是
    /// 0×0，而且不报错。
    /// </remarks>
    public void Register(UISurface surface, Control control)
    {
        if (!_surfaces.TryAdd(surface.Id, control))
        {
            throw new InvalidOperationException($"界面 id 重复登记：{surface.Id}");
        }

        control.Visible = false;
        LayerOf(surface.Layer).AddChild(control);
        control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    /// <summary>打开一个界面。关卡内不可用的会被拒绝并说明原因，而不是静默不动。</summary>
    public bool Open(UISurface surface)
    {
        if (Context == UIContext.Level && !surface.AvailableInLevel)
        {
            GD.Print($"[界面] 关卡内不可用：{surface.Id} —— 它是管理功能，关卡里只许查看");
            return false;
        }

        _nav.Push(surface);
        Sync();
        return true;
    }

    /// <summary>关掉一个界面。</summary>
    public void Close(UISurface surface)
    {
        _nav.Close(surface);
        Sync();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 只处理返回。栈空时不消费这次输入，交给上层去开暂停菜单 —— 吞掉的表现是「按了没反应」，
        // 玩家会以为卡死。
        if (@event.IsActionPressed("ui_cancel") && _nav.HandleBack())
        {
            Sync();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>把导航栈的状态同步到节点：谁可见、要不要暂停。</summary>
    private void Sync()
    {
        foreach (var (id, control) in _surfaces)
        {
            control.Visible = _nav.Surfaces.Any(s => s.Id == id);
        }

        // 暂停由整个栈决定，不由某个面板自己设：弹出界面接管输入就暂停世界，所以栈里有东西就暂停。
        // 这个判定在 NavigationStack，本类只负责把它翻译成 SceneTree.Paused。
        GetTree().Paused = _nav.WorldShouldPause;
    }
}
