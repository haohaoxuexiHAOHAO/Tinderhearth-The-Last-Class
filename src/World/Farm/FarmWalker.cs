using Godot;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>
/// 俯视场景里走动的角色。**它与侧视关卡那个 <see cref="PlayerActor"/> 是两套动作机。**
/// </summary>
/// <remarks>
/// 两套不合成一套，理由是轴不一样：侧视关卡是「带连续可行走纵深的横版」，纵深是一条独立的轴、
/// 位置归规则层持有（代码仓 `ARCHITECTURE.md` 那张三轴表）；俯视没有独立纵深轴 ——
/// **屏幕的纵向就是纵深**（[场景绘制约定 · 前后遮挡按脚底位置排序]）。硬凑成一个类会让纵深
/// 那套钳制在俯视场景里无处可落。
///
/// **本类只管走与朝向。** 它不碰地块、不碰当前操作格；那两样各在
/// <see cref="FarmField"/> 与 <see cref="FarmCellCursor"/>。
///
/// 按 `ADR-0009`，节点树与那两个参数值都归作者：本类不自己建子节点，缺哪一样当场报错说清缺谁。
/// </remarks>
public partial class FarmWalker : CharacterBody2D
{
    /// <summary>输入门面。**玩法代码一律经它问输入**，不直接轮询 <c>Input</c>（`CONVENTIONS.md`）。</summary>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>
    /// 走动速度，世界像素每秒。
    /// </summary>
    /// <remarks>
    /// **刻意没有默认值。** 它是手感量，归 `GP-6` 实测收敛（登记在设计仓 `design/数值模型.md`
    /// 的尚未给值表），所以代码里给一个「看起来还行」的数就是把猜出来的值伪装成配置（`ADR-0009`）。
    /// 填 0 或负数当场报错。
    /// </remarks>
    [Export]
    public float SpeedPixelsPerSecond { get; set; }

    /// <summary>
    /// 角色此刻朝哪一向。取值只有上下左右四个之一。
    /// </summary>
    /// <remarks>
    /// **四向而不是八向**，理由在[场景绘制约定 · 俯视角色画四个方向]：八向把每个角色的俯视动画量
    /// 翻一倍，而斜向走动时四向贴图配上实际位移方向玩家读不出缺了什么。
    ///
    /// 手柄那一侧的当前操作格靠它算（[`界面系统` · 世界里那一层：当前操作格，两个设备族共用一个读口]），
    /// 所以它是**对外的读口**、不是内部状态。
    /// </remarks>
    public Vector2I Facing { get; private set; } = Vector2I.Down;

    private InputRouter _router = null!;

    public override void _Ready()
    {
        _router = Router ?? throw new InvalidOperationException(
            $"{nameof(FarmWalker)}（节点 {Name}）的 {nameof(Router)} 没接上 —— "
                + $"请在场景里放一个挂了 {nameof(InputRouter)} 脚本的节点，再把它拖到这一格上");

        if (SpeedPixelsPerSecond <= 0f)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmWalker)}（节点 {Name}）的 {nameof(SpeedPixelsPerSecond)} 是 "
                    + $"{SpeedPixelsPerSecond} —— 这个量没有默认值，要在检查器里填一个大于零的数"
                    + "（它归 `GP-6` 实测调，填多少由你实机试）");
        }

        // 俯视没有地板与天花板的概念，所以用浮空模式。用接地模式会把「往上走」当成跳 ——
        // 表现是纵向移动卡顿或直接不动，而引擎不报错。
        MotionMode = MotionModeEnum.Floating;
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 direction = _router.MoveDirection();
        Velocity = direction * SpeedPixelsPerSecond;
        UpdateFacing(direction);
        MoveAndSlide();
    }

    /// <summary>
    /// 按这一帧的输入更新朝向。**某个轴更强时才换，相等时不变。**
    /// </summary>
    /// <remarks>
    /// 斜着按时两个分量相等（<see cref="InputRouter.MoveDirection"/> 归一化过），此时**保持上一个
    /// 朝向**：强行选一个会让角色在斜着走时朝向来回跳，而那一跳会连着把当前操作格也甩到另一格上，
    /// 玩家按下去就作用错了地方。静止时同理不变 —— 松开方向键不该让他转身。
    /// </remarks>
    private void UpdateFacing(Vector2 direction)
    {
        var horizontal = Mathf.Abs(direction.X);
        var vertical = Mathf.Abs(direction.Y);

        if (horizontal > vertical)
        {
            Facing = direction.X > 0f ? Vector2I.Right : Vector2I.Left;
        }
        else if (vertical > horizontal)
        {
            Facing = direction.Y > 0f ? Vector2I.Down : Vector2I.Up;
        }
    }
}
