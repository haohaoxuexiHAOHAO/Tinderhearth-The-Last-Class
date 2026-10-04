using Godot;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>俯视场景里走动的角色。只管移动和朝向。</summary>
/// <remarks>
/// 它和侧视关卡那个 <see cref="PlayerActor"/> 是两套，不合并。原因是轴不一样：侧视关卡的纵深是
/// 一条独立的轴、位置由规则层持有；俯视没有独立纵深轴，屏幕的纵向就是纵深。硬凑成一个类之后，
/// 侧视那套纵深钳制在俯视场景里无处可落。
///
/// 地块和当前操作格不在这里，分别在 <see cref="FarmField"/> 和 <see cref="FarmCellCursor"/>。
/// </remarks>
public partial class FarmWalker : CharacterBody2D
{
    /// <summary>输入门面。玩法代码一律经它问输入，不直接轮询 <c>Input</c>。</summary>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>走动速度，世界像素每秒。</summary>
    /// <remarks>
    /// 没有默认值，必须在检查器里填，填 0 或负数当场报错。它是手感量，只能实机调出来，所以
    /// 代码里给一个「看起来还行」的数等于把猜的值伪装成配置。
    /// </remarks>
    [Export]
    public float SpeedPixelsPerSecond { get; set; }

    /// <summary>角色此刻朝哪一向，取值只有上下左右四个之一。</summary>
    /// <remarks>
    /// 四向而不是八向：八向会把每个角色的俯视动画量翻一倍，而斜着走时四向贴图配上实际位移方向，
    /// 玩家读不出缺了什么。
    ///
    /// 手柄时的当前操作格靠它算，所以它是给外面读的，不是内部状态。
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
                    + $"{SpeedPixelsPerSecond}，这个量没有默认值，要在检查器里填一个大于零的数"
                    + "（填多少实机试）");
        }

        // 俯视没有地板和天花板，所以用浮空模式。接地模式会把「往上走」当成跳，表现是纵向移动
        // 卡顿或者干脆不动，而引擎不报错。
        MotionMode = MotionModeEnum.Floating;
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 direction = _router.MoveDirection();
        Velocity = direction * SpeedPixelsPerSecond;
        UpdateFacing(direction);
        MoveAndSlide();
    }

    /// <summary>按这一帧的输入更新朝向。某个轴更强时才换，两轴相等时不变。</summary>
    /// <remarks>
    /// 斜着按时两个分量相等（<see cref="InputRouter.MoveDirection"/> 归一化过），这时保持上一个
    /// 朝向。强行选一个会让角色在斜着走时朝向来回跳，而那一跳会连着把当前操作格甩到另一格上，
    /// 玩家按下去就作用错了地方。静止时同理，松开方向键不该让他转身。
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
