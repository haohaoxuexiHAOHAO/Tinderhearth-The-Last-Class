using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>不还手的受击木桩；唯一拥有其状态推进。</summary>
public partial class TrainingDummy : CharacterBody2D, IDepthActor
{
    /// <summary>统一状态载体。</summary>
    public StatusEffects Statuses { get; } = new();
    /// <summary>剩余闪白逻辑帧。</summary>
    public int FlashRemaining { get; private set; }
    /// <summary>已接受命中次数，开发探针用。</summary>
    public int HitCount { get; private set; }
    private float _step;
    private double _depthWorldPx = DepthBand.CenterWorldPx;
    private Polygon2D _post = null!;
    private Polygon2D _arm = null!;
    private static readonly Color PostColor = new("b85450");
    private static readonly Color ArmColor = new("d99863");

    /// <summary>柱子宽度，世界像素。碰撞框、绘制与影子范围共用它，不各写一个 18。</summary>
    private const int PostWidthWorldPx = 18;

    /// <summary>
    /// 纵深可视根（`ENG-15`）：木桩的两块几何挂在它下面，纵深偏移与影子都由它管。
    /// </summary>
    public DepthVisual Visual { get; private set; } = null!;

    /// <inheritdoc />
    /// <remarks>
    /// 木桩不建 <see cref="MotorState"/>（`SPEC` §3.2：不建无用的 Motor），所以纵深就存在这里，
    /// **这是它的唯一一份**。将来命中容差（`GP-16`）读的也是它，不再存第二份。
    /// </remarks>
    public double DepthWorldPx => _depthWorldPx;

    /// <inheritdoc />
    public DepthSubject DepthSubject => Visual.Subject;

    /// <inheritdoc />
    /// <remarks>木桩是几何体、没有精灵，本体范围就是那根柱子（18px 宽、以脚底为中心）。</remarks>
    public GroundSpan BodySpanWorldPx => GroundSpan.Centered(PostWidthWorldPx);

    /// <summary>把木桩摆到带内某个纵深上。带外的值被钳进带内。</summary>
    public void PlaceDepth(double depthWorldPx)
    {
        _depthWorldPx = DepthBand.Clamp(depthWorldPx);
        Visual?.Sync();
    }

    public override void _Ready()
    {
        CollisionLayer = 4;
        CollisionMask = 1;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(PostWidthWorldPx, 32) },
            Position = new Vector2(0, -16),
        });
        AddChild(new Hurtbox { Actor = this });
        Visual = new DepthVisual { Actor = this };
        AddChild(Visual);
        // 身体从 `_Draw` 改成挂在可视根下的几何（`ENG-15`）：纵深偏移因此只有一处来源，
        // 而且影子（画在可视根自己的 `_Draw` 里）天然排在身体下面，不必再管两者的先后。
        const int half = PostWidthWorldPx / 2;
        _post = new Polygon2D
        {
            Polygon = [new(-half, -32), new(half, -32), new(half, 0), new(-half, 0)],
            Color = PostColor,
        };
        _arm = new Polygon2D
        {
            Polygon = [new(-13, -22), new(13, -22), new(13, -17), new(-13, -17)],
            Color = ArmColor,
        };
        Visual.AddChild(_post);
        Visual.AddChild(_arm);
    }

    /// <summary>新命中替换剩余硬直与位移，不叠加。</summary>
    public void Receive(HitReaction reaction, int facing)
    {
        Statuses.Apply(StatusKind.Hitstun, reaction.HitstunFrames);
        _step = facing * (float)reaction.KnockbackWorldPx / reaction.HitstunFrames;
        FlashRemaining = CombatFeel.FlashFrames;
        HitCount++;
        ApplyFlash();
    }

    /// <summary>旧硬直每帧消费一次位移；第 N 次消费后到期，无尾滑。</summary>
    public void AdvanceCombat()
    {
        var moving = Statuses.Has(StatusKind.Hitstun);
        Statuses.Tick();
        if (moving) MoveAndCollide(new Vector2(_step, 0));
        if (!Statuses.Has(StatusKind.Hitstun)) _step = 0;
        if (FlashRemaining > 0) FlashRemaining--;
        // 位置已定才刷纵深偏移与影子：射线要问的是这一帧的最终位置。
        Visual.Sync();
        ApplyFlash();
    }

    /// <summary>
    /// 闪白：直接换两块几何的填充色，与原来 `_Draw` 里按 <see cref="FlashRemaining"/> 选色等价。
    /// </summary>
    /// <remarks>
    /// **不能用 <c>Modulate</c>**：它是乘法，白色乘原色还是原色，闪白会静默失效（画面上木桩根本
    /// 不变白，而所有逻辑判据照旧全绿）。`GP-13` 的闪白取色判据量的是屏幕像素，正是这条改动的
    /// 量具 —— 换画法之后它必须仍然读到纯白。
    /// </remarks>
    private void ApplyFlash()
    {
        var flashing = FlashRemaining > 0;
        _post.Color = flashing ? Colors.White : PostColor;
        _arm.Color = flashing ? Colors.White : ArmColor;
    }
}
