using Godot;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>俯视场景里走动的角色。管移动、朝向，以及按朝向切精灵。</summary>
/// <remarks>
/// 它和侧视关卡那个 <see cref="PlayerActor"/> 是两套，不合并。原因是轴不一样：侧视关卡的纵深是
/// 一条独立的轴、位置由规则层持有；俯视没有独立纵深轴，屏幕的纵向就是纵深。硬凑成一个类之后，
/// 侧视那套纵深钳制在俯视场景里无处可落。
///
/// 地块和当前操作格不在这里，分别在 <see cref="FarmField"/> 和 <see cref="FarmCellCursor"/>。
///
/// 接地影子也不在这里，它画进素材本身的每一帧里。原先这个类画过一块几何椭圆，删掉的理由是
/// 它不跟着走路变形：腿迈开而影子钉在正中，人看起来在影子上横向滑。影子进了素材之后形状逐帧
/// 由作者定，而且天然落在整像素上。代价是它不再知道脚下是什么地面，所以那个色要挑在比所有
/// 地面都暗一截的区间里 —— 色板里有一条专门的影子色带。
/// </remarks>
public partial class FarmWalker : CharacterBody2D
{
    private const string IdleAction = "idle";
    private const string WalkAction = "walk";

    /// <summary>代码会请求的动作，与下面那组朝向配对成全部动画名。</summary>
    /// <remarks>
    /// 载入时核的就是这两组的配对结果，所以「代码请求的」与「载入核过的」永远是同一份 ——
    /// 另写一份清单的话会出现核了八个却请求第九个，而那一下要等玩家真的转到那一向才炸。
    /// </remarks>
    private static readonly string[] Actions = [IdleAction, WalkAction];

    private static readonly Vector2I[] Facings =
        [Vector2I.Down, Vector2I.Up, Vector2I.Left, Vector2I.Right];

    /// <summary>输入门面。玩法代码一律经它问输入，不直接轮询 <c>Input</c>。</summary>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>角色的精灵。它那份 SpriteFrames 由作者在编辑器里建，这里只负责切到哪一个。</summary>
    /// <remarks>
    /// 动画名是「动作_朝向」，朝向取 down、up、left、right 四个词之一，所以这份表要有八个动画：
    /// idle 与 walk 各四向。缺哪一个在 <see cref="_Ready"/> 里点名报错。
    ///
    /// 每个朝向各一套，代码不做水平翻转，也没有「这个角色翻不翻」的开关 —— 主角缺一条手臂，
    /// 镜像会让空袖子每次转身换边。想省就在绘图软件里镜像后导出，那是绘制决定，代码看到的只是
    /// 两套长得镜像的图。理由在设计仓 decisions/ADR-0021-每个朝向各画一套不做水平翻转.md。
    /// </remarks>
    [Export]
    public AnimatedSprite2D? Body { get; set; }

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
    private AnimatedSprite2D _body = null!;

    // 上一次切动画时的动作与朝向。Play 会把帧号与帧内进度归零，所以每帧调一次等于动画永远停在
    // 第 0 帧 —— 而那不报错，只表现为「角色站着不动也不呼吸」。
    private string _playingAction = "";
    private Vector2I _playingFacing;

    public override void _Ready()
    {
        // 先停掉每帧回调，全部校验过了再开。_Ready 抛出之后引擎并不会停掉这个节点 ——
        // _PhysicsProcess 照旧每帧跑，而下面那几个字段还是 null，于是一条说清缺了什么的报错会被
        // 每秒六十条空引用盖掉。不关的话输出面板里有几百条，而真正该看的那一条在最顶上。
        SetPhysicsProcess(false);

        _router = Router ?? throw new InvalidOperationException(
            $"{nameof(FarmWalker)}（节点 {Name}）的 {nameof(Router)} 没接上 —— "
                + $"请在场景里放一个挂了 {nameof(InputRouter)} 脚本的节点，再把它拖到这一格上");

        _body = Body ?? throw new InvalidOperationException(
            $"{nameof(FarmWalker)}（节点 {Name}）的 {nameof(Body)} 没接上 —— "
                + "请在它下面放一个 AnimatedSprite2D，再把那个节点拖到这一格上");

        if (SpeedPixelsPerSecond <= 0f)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmWalker)}（节点 {Name}）的 {nameof(SpeedPixelsPerSecond)} 是 "
                    + $"{SpeedPixelsPerSecond}，这个量没有默认值，要在检查器里填一个大于零的数"
                    + "（填多少实机试）");
        }

        if (_body.SpriteFrames is null)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmWalker)}（节点 {Name}）接的那个精灵（{_body.Name}）还没有 SpriteFrames —— "
                    + "在检查器里新建一份，再到底部那个面板里按「动作_朝向」加动画");
        }

        string[] missing = [.. Actions
            .SelectMany(action => Facings.Select(facing => AnimationName(action, facing)))
            .Where(name => !_body.SpriteFrames.HasAnimation(name))];
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmWalker)}（节点 {Name}）接的那份 SpriteFrames 缺这几个动画："
                    + $"{string.Join("、", missing)} —— 每个朝向各要一套，"
                    + "代码不拿另一向镜像凑；想省画量就在绘图软件里镜像后导出");
        }

        // 俯视没有地板和天花板，所以用浮空模式。接地模式会把「往上走」当成跳，表现是纵向移动
        // 卡顿或者干脆不动，而引擎不报错。
        MotionMode = MotionModeEnum.Floating;

        Play(IdleAction, Facing);

        SetPhysicsProcess(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 direction = _router.MoveDirection();
        Velocity = direction * SpeedPixelsPerSecond;
        UpdateFacing(direction);
        MoveAndSlide();
        // 按输入判在不在走，不按移动后的速度判。顶着障碍物推的时候仍然播行走 —— 改用实际位移的话
        // 贴着墙按方向键会显示待机，玩家会以为自己没按上。
        Play(direction == Vector2.Zero ? IdleAction : WalkAction, Facing);
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

    /// <summary>切到这个动作与朝向对应的那个动画。动作与朝向都没变时一个字不做。</summary>
    private void Play(string action, Vector2I facing)
    {
        if (action == _playingAction && facing == _playingFacing)
        {
            return;
        }
        _playingAction = action;
        _playingFacing = facing;
        _body.Play(AnimationName(action, facing));
    }

    /// <summary>一个动作加一个朝向对应哪个动画名。</summary>
    private static string AnimationName(string action, Vector2I facing) =>
        $"{action}_{FacingWord(facing)}";

    /// <summary>朝向对应的那个英文词。这四个词只在这里定，别处都从这里取。</summary>
    /// <remarks>
    /// 与输入动作名（<see cref="InputActions.MoveDown"/> 那一组）用同一组词，所以「按下的键」与
    /// 「播的动画」读起来对得上，不是两套词。
    /// </remarks>
    private static string FacingWord(Vector2I facing) => facing switch
    {
        _ when facing == Vector2I.Up => "up",
        _ when facing == Vector2I.Left => "left",
        _ when facing == Vector2I.Right => "right",
        _ => "down",
    };
}
