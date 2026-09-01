using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 世界空间 UI 的启动自检（`UI-9`）。**证明规则层测试证明不了的那半 —— 引擎侧真的挂对了层、
/// 跟得上目标、按状态显隐。**
/// </summary>
/// <remarks>
/// 规则层测试（<c>WorldSpaceTests</c>）已经钉住了血条只精英、伤害默认关、进度钳制这些**判定**；
/// 这里补的是引擎侧的事实：读条圆环的父节点是 <see cref="UiLayer.WorldSpace"/> 那一层（不是 Hud）、
/// 该层开了 <c>FollowViewportEnabled</c>（缩放跟随靠它）、圆环位置跟上目标、受击中断即隐藏、
/// 伤害数字默认关。判据打进日志，`tools/check_worldui.py` 读回来判。
///
/// **不需要窗口**：全是逻辑与节点关系检查，不截图，所以 headless 下照样跑（与 <see cref="HudProbe"/>、
/// <see cref="Tinderhearth.World.CameraProbe"/> 不同，那两个要真窗口）。因此它排在探针链**最前面**、
/// 在任何相机出现之前跑 —— 那时世界空间层的画布变换是恒等，圆环位置就等于目标世界坐标，测起来干净。
/// 跑完收掉自己的元素、让层回到干净状态，再 <see cref="Finished"/> 触发相机自检。
/// </remarks>
public sealed partial class WorldSpaceProbe : Node
{
    // 任意一个世界坐标，用来验「圆环跟上目标」。是探针的测量夹具，不是布局数。
    private static readonly Vector2 TargetPos = new(123, 87);
    private const int DemoDamage = 12;

    private readonly UiRoot _ui;
    private WorldSpaceUi _world = null!;
    private Node2D _target = null!;
    private WorldHealthBar _eliteBar = null!;
    private WorldHealthBar _trashBar = null!;
    private int _frame;

    public WorldSpaceProbe(UiRoot ui) => _ui = ui;

    /// <summary>测完触发，串起相机自检。</summary>
    public event System.Action? Finished;

    public override void _Ready()
    {
        _target = new Node2D { Name = "ProbeTarget", Position = TargetPos };
        AddChild(_target);

        _world = new WorldSpaceUi(_ui);
        AddChild(_world);
        _world.TrackCast(_target);
        _world.SetCast(new CastState(true, 0.5, false));
        _eliteBar = _world.AttachBar(_target, new EliteHealth(10, 20, EnemyRank.Elite));
        _trashBar = _world.AttachBar(_target, new EliteHealth(10, 20, EnemyRank.Trash));
    }

    public override void _Process(double delta)
    {
        // 等一帧让元素的 _Process 跑过、位置更新到目标身上，再测。
        if (++_frame < 2)
        {
            return;
        }

        Measure();

        _world.QueueFree();     // _ExitTree 收掉挂在层上的圆环与血条
        _target.QueueFree();
        SetProcess(false);
        Finished?.Invoke();
        QueueFree();
    }

    private void Measure()
    {
        var pass = 0;
        var total = 0;

        void Judge(string tag, string what, bool ok, string detail)
        {
            total++;
            if (ok)
            {
                pass++;
            }

            GD.Print("[世界UI] 判据 ", ok ? "PASS" : "FAIL", " ｜ ", tag, " ｜ ", what, " ｜ ", detail);
        }

        var layer = _ui.LayerOf(UiLayer.WorldSpace);
        Judge("挂载层", "读条圆环挂在世界空间层而非 Hud",
            _world.Ring.GetParent() == layer && layer.Layer == (int)UiLayer.WorldSpace,
            $"父 {_world.Ring.GetParent()?.Name}｜层号 {layer.Layer}");
        Judge("缩放跟随", "世界空间层开了 FollowViewport",
            layer.FollowViewportEnabled, layer.FollowViewportEnabled.ToString());
        Judge("跟随", "读条圆环位置跟上目标世界坐标",
            _world.Ring.Position == _target.GlobalPosition,
            $"环 {_world.Ring.Position}｜目标 {_target.GlobalPosition}");

        _world.SetCast(new CastState(true, 0.5, true));   // 受击中断
        Judge("中断", "受击中断后读条隐藏",
            !_world.Ring.Visible, $"可见 {_world.Ring.Visible}");

        Judge("血条精英", "精英出血条、杂兵不出",
            _eliteBar.ShowsBar && _eliteBar.Visible && !_trashBar.Visible,
            $"精英可见 {_eliteBar.Visible}｜杂兵可见 {_trashBar.Visible}");

        _world.SetOptions(WorldUiOptions.Default);        // 默认关
        _world.PopDamage(_target, DemoDamage);
        var offBlocked = _world.LiveDamageCount == 0;
        _world.SetOptions(new WorldUiOptions(ShowDamageNumbers: true));
        _world.PopDamage(_target, DemoDamage);
        var onPops = _world.LiveDamageCount == 1;
        Judge("伤害数字", "默认关、开了才冒", offBlocked && onPops,
            $"关时冒 {(offBlocked ? 0 : 1)}｜开后冒 {_world.LiveDamageCount}");

        Judge("字体", "世界文字用像素字体 12px",
            ThemeDB.FallbackFont is not null && ThemeDB.FallbackFontSize == UiMetrics.FontSize,
            $"回退字号 {ThemeDB.FallbackFontSize}");

        GD.Print("[世界UI] 自检 ", pass, "/", total, " 条通过");
    }
}
