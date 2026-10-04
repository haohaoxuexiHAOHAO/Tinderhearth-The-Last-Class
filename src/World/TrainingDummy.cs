using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.World;

/// <summary>不还手的受击木桩：一根立着的桩子，挨打会硬直、被击退、闪一下白。</summary>
/// <remarks>
/// 它是开发期用来试命中与击退的几何占位。训练房里的靶已经换成真角色了，所以受击表现那一套
/// 看角色那一侧；这里留的是一个不依赖美术的最简目标。
/// </remarks>
public partial class TrainingDummy : CharacterBody2D, IBlockingActor, IHittable
{
    /// <inheritdoc />
    /// <remarks>
    /// 木桩是中立的：一根立在那儿的桩子不站边，所以对谁都挡。它与主角之间的单向碰撞
    /// （自己在层 4、只看层 1）是另一回事 —— 那条让木桩被击退时撞上主角会停住。
    /// </remarks>
    public CombatSide Side { get; init; } = CombatSide.Neutral;

    /// <summary>硬直这类状态都存在这里。</summary>
    public StatusEffects Statuses { get; } = new();

    /// <summary>闪白还剩几帧。</summary>
    public int FlashRemaining { get; private set; }

    /// <summary>总共挨了几下。</summary>
    public int HitCount { get; private set; }
    private float _step;
    private double _depthWorldPx = DepthBand.CenterWorldPx;
    private Polygon2D _post = null!;
    private Polygon2D _arm = null!;
    private static readonly Color PostColor = new("b85450");
    private static readonly Color ArmColor = new("d99863");

    /// <summary>柱子宽度，世界像素。碰撞框、绘制与影子范围共用它，不各写一份。</summary>
    private const int PostWidthWorldPx = 18;

    /// <summary>柱子高度，世界像素。碰撞框、绘制与受击框共用它，不各写一份。</summary>
    /// <remarks>
    /// 木桩的受击框本来就该等于它自己那根柱子，而这个数原先在三个地方各写了一遍（碰撞、绘制、
    /// 受击框）。三份相等全靠人记得，改柱子高度漏改一处不报错 —— 表现是「看着这么高、打起来不是
    /// 这么高」。收成一处之后，看着对就是因为它真的对。
    /// </remarks>
    private const int PostHeightWorldPx = 32;

    /// <summary>纵深可视根：木桩的两块几何挂在它下面，纵深偏移与影子都由它管。</summary>
    public DepthVisual Visual { get; private set; } = null!;

    /// <summary>受击区域。调试叠层从它身上取几何画框。</summary>
    public Hurtbox Hurtbox { get; private set; } = null!;

    /// <inheritdoc />
    /// <remarks>
    /// 木桩不自己走路，所以不给它建一套运动状态，纵深就存在本类的字段里 —— 这是它唯一的一份。
    /// 绘制排序与命中的纵深条件读的都是它，都经这个接口。
    /// </remarks>
    public double DepthWorldPx => _depthWorldPx;

    /// <inheritdoc />
    public DepthSubject DepthSubject => Visual.Subject;

    /// <inheritdoc />
    /// <remarks>它是几何体、没有精灵，所以贴地范围就是那根柱子，以脚底为中心。</remarks>
    public GroundSpan BodySpanWorldPx => GroundSpan.Centered(PostWidthWorldPx);

    /// <summary>把木桩摆到某个纵深上。超出可行走纵深范围的值会被钳回范围内。</summary>
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
            Shape = new RectangleShape2D { Size = new Vector2(PostWidthWorldPx, PostHeightWorldPx) },
            Position = new Vector2(0, -PostHeightWorldPx / 2),
        });
        Hurtbox = new Hurtbox { Actor = this, HeightWorldPx = PostHeightWorldPx };
        AddChild(Hurtbox);
        Visual = new DepthVisual { Actor = this };
        AddChild(Visual);
        // 身体做成挂在可视根下的几何节点，而不是在 _Draw 里画：纵深偏移因此只有一处来源，
        // 而且影子（画在可视根自己的 _Draw 里）天然排在身体下面，不必再管两者的先后。
        const int half = PostWidthWorldPx / 2;
        _post = new Polygon2D
        {
            Polygon = [new(-half, -PostHeightWorldPx), new(half, -PostHeightWorldPx),
                       new(half, 0), new(-half, 0)],
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

    /// <summary>挨一下。新的一下直接顶掉还剩的硬直与击退位移，不往上叠加。</summary>
    public void Receive(HitReaction reaction, int facing)
    {
        Statuses.Apply(StatusKind.Hitstun, reaction.HitstunFrames);
        _step = facing * (float)reaction.KnockbackWorldPx / reaction.HitstunFrames;
        FlashRemaining = CombatFeel.FlashFrames;
        HitCount++;
        ApplyFlash();
    }

    /// <summary>推进一帧：硬直期间每帧挪一步击退位移，硬直走完就停住，不会多滑一下。</summary>
    public void AdvanceCombat()
    {
        var moving = Statuses.Has(StatusKind.Hitstun);
        Statuses.Tick();
        if (moving) MoveAndCollide(new Vector2(_step, 0));
        if (!Statuses.Has(StatusKind.Hitstun)) _step = 0;
        if (FlashRemaining > 0) FlashRemaining--;
        // 位置定下来之后才刷纵深偏移与影子：找地面那条射线要问的是这一帧的最终位置。
        Visual.Sync();
        ApplyFlash();
    }

    /// <summary>闪白：直接把两块几何的填充色换成白色。</summary>
    /// <remarks>
    /// 不能用 <c>Modulate</c>：它是乘法，白色乘上原色还是原色，闪白会静默失效 —— 画面上木桩根本
    /// 不变白，而逻辑上一切正常。木桩能靠换颜色变白，是因为它本来就是几何；有贴图的角色得走着色器。
    /// </remarks>
    private void ApplyFlash()
    {
        var flashing = FlashRemaining > 0;
        _post.Color = flashing ? Colors.White : PostColor;
        _arm.Color = flashing ? Colors.White : ArmColor;
    }
}
