using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 界面根节点（`UI-6`）：六层画布、导航栈、返回键与暂停。**层级只在这里定义一次。**
/// </summary>
/// <remarks>
/// 分工是刻意的：**规则在规则层，节点在这里。** 「按返回键回到哪」「关卡内哪些页能用」
/// 「打开这一层要不要暂停」都由 <see cref="NavigationStack"/> 与 <see cref="UiSurface"/> 判，
/// 有单元测试盯着；本类只负责把判定结果翻译成节点的显隐与 <c>SceneTree.Paused</c>。
///
/// 为什么不让各场景自己摆 CanvasLayer：那样每个界面会自己挑一个层号，迟早出现「弹窗被 HUD
/// 挡住」，而排查时得翻遍所有场景才知道谁用了哪个号。
///
/// <see cref="UiLayer.WorldSpace"/> 那一层开了 <c>FollowViewportEnabled</c> —— 它要跟着相机
/// 走并随缩放变化，因为正典要求读条画在执行者身上而不是界面角落。
/// </remarks>
public partial class UiRoot : Node
{
    private readonly Dictionary<UiLayer, CanvasLayer> _layers = [];
    private readonly Dictionary<string, Control> _surfaces = [];
    private readonly NavigationStack _nav = new();

    /// <summary>当前场合。切场景时由场景设置，决定手环哪些页可用。</summary>
    public UiContext Context { get; set; } = UiContext.Base;

    /// <summary>导航栈。只读暴露给需要查询的地方，压弹一律走本类的方法。</summary>
    public NavigationStack Navigation => _nav;

    public override void _Ready()
    {
        foreach (UiLayer layer in Enum.GetValues<UiLayer>())
        {
            if (layer == UiLayer.World)
            {
                continue;   // 世界不是 UI 层，它就是场景本身
            }

            var canvas = new CanvasLayer
            {
                Name = layer.ToString(),
                Layer = (int)layer,
                // 世界空间那一层跟随相机；其余是屏幕空间，固定不动。
                FollowViewportEnabled = layer == UiLayer.WorldSpace,
            };
            AddChild(canvas);
            _layers[layer] = canvas;
        }

        GD.Print("[界面] 层级 ", string.Join(" → ",
            Enum.GetValues<UiLayer>().Select(l => $"{l}({(int)l})")));
        GD.Print("[界面] 排版单位 栅格 ", UiMetrics.Grid,
                 "｜内边距 ", UiMetrics.PanelPadding,
                 "｜间距 ", UiMetrics.ItemGap,
                 "｜安全边距 ", UiMetrics.SafeMargin,
                 "｜图标 ", UiMetrics.IconSmall, "/", UiMetrics.IconLarge,
                 "｜满宽汉字下限 ", UiMetrics.MaxFullWidthChars);
    }

    /// <summary>取某一层的画布，用来往上挂节点。</summary>
    public CanvasLayer LayerOf(UiLayer layer) =>
        _layers.TryGetValue(layer, out var canvas)
            ? canvas
            : throw new ArgumentOutOfRangeException(nameof(layer), $"没有这一层：{layer}");

    /// <summary>
    /// 登记一个界面：把它挂到自己声明的层上，初始隐藏。
    /// </summary>
    /// <remarks>
    /// 布局一律靠锚点与容器 —— 这里强制铺满所属层，具体位置由界面内部的容器决定。
    /// 正典要求不写死绝对像素坐标，理由是 `aspect="expand"` 下逻辑宽度会变（`UI-3` 实测：
    /// 3840×2130 的窗口得到逻辑 649×360），写死坐标的界面在宽窗口上会错位。
    /// </remarks>
    public void Register(UiSurface surface, Control control)
    {
        if (!_surfaces.TryAdd(surface.Id, control))
        {
            throw new InvalidOperationException($"界面 id 重复登记：{surface.Id}");
        }

        control.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        control.Visible = false;
        LayerOf(surface.Layer).AddChild(control);
    }

    /// <summary>打开一层。关卡内不可用的层会被拒绝并说明原因，而不是静默不动。</summary>
    public bool Open(UiSurface surface)
    {
        if (Context == UiContext.Level && !surface.AvailableInLevel)
        {
            GD.Print($"[界面] 关卡内不可用：{surface.Id}（管理功能，见玩法定位 · 跨系统约定）");
            return false;
        }

        _nav.Push(surface);
        Sync();
        return true;
    }

    /// <summary>关掉指定层。</summary>
    public void Close(UiSurface surface)
    {
        _nav.Close(surface);
        Sync();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 只处理返回：栈空时**不消费**这次输入，交给上层去开暂停菜单。
        // 吞掉的表现是「按了没反应」，玩家会以为卡死（NavigationStack 有测试盯这条）。
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

        // 暂停由栈整体决定，不由某个面板自己设 —— 散落着设迟早有人顺手给背包加一句暂停，
        // 而正典明确要求关卡内背包操作不暂停。
        GetTree().Paused = _nav.WorldShouldPause;
    }
}
