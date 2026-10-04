using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// 一个占着纵深的角色。纵深值只有一份，实现者从自己那唯一一处转发出来，不另存一份。
/// </summary>
/// <remarks>
/// 绘制排序与命中判定必须读同一份纵深值 —— 排错了，玩家看到的前后关系会与打得着打不着相反。
/// 这个接口就是那句「同一份」在类型上的落点：排序与命中容差都经它取值。
/// 为什么战斗关卡有这么一条纵深轴，见设计仓 canon/gameplay/战斗与关卡.md 的
/// 「战斗关卡的空间模型：带纵深的横版」一节。
/// </remarks>
public interface IDepthActor
{
    /// <summary>纵深位置，世界像素。坐标口径见 <see cref="DepthBand"/>（0 最靠后）。</summary>
    double DepthWorldPx { get; }

    /// <summary>
    /// 当前姿态贴着地面的那一段水平范围，世界像素，相对脚底锚点、已经含了朝向镜像。影子按它取宽窄。
    /// </summary>
    /// <remarks>
    /// 给一段范围而不是给一个宽度：只有宽度的话影子只能对称画在脚底上，出拳那一帧就会与身体错开。
    /// 「本体」怎么量交给实现者 —— 有精灵的按当前帧的不透明边界量，几何体按自己的尺寸。放进接口
    /// 而不是让影子自己猜，是因为只有角色知道自己此刻摆的是什么姿态。
    /// </remarks>
    GroundSpan BodySpanWorldPx { get; }

    /// <summary>排序要用的那两个量。实现者转发自己的 <see cref="DepthVisual.Subject"/>，不自己拼。</summary>
    /// <remarks>
    /// 放进接口，而不是让排序层去角色的子节点里找 <see cref="DepthVisual"/>：找的那一步每帧都要
    /// 遍历一遍子节点，而且「找不到就改用角色自己的 Y」这条兜底会悄悄给出错的顺序 —— 跳跃高度
    /// 会混进本该只有地面高度的那个量里。
    /// </remarks>
    DepthSubject DepthSubject { get; }
}

/// <summary>
/// 角色的纵深可视根：按纵深把画面上下挪一段、画影子、报排序要用的量。
/// </summary>
/// <remarks>
/// 角色的可视子节点都挂在这里，不挂在角色本体上。于是「纵深偏移」只有一处来源（本节点的
/// <c>Position</c>），新增一种角色时漏不掉 —— 漏了的表现是那个角色在纵深上走动时画面不动，
/// 而它不报错。角色本体只留物理：它的 <c>Position</c> 仍然只管横向与跳跃高度。
///
/// 地面靠射线每帧问一次，不靠「记住上次站在多高」。影子要落在角色正下方的地面上，而地形有起伏
/// 和坑；记忆式的做法在跳过坑时会把影子留在坑沿的高度 —— 那时影子指的位置是错的，而画面上只是
/// 「影子有点怪」。射线问的是真实地形，坑底就是坑底。
/// </remarks>
public partial class DepthVisual : Node2D
{
    private static readonly Color ShadowColor = PixelTheme.ToColor(HudPalette.Charcoal);

    /// <summary>影子那个椭圆用几个顶点画。</summary>
    /// <remarks>
    /// 16 个在这个逻辑分辨率下看着已经够圆，再多的顶点在几像素宽的影子上看不出区别。
    /// </remarks>
    private const int ShadowVertices = 16;

    private readonly Vector2[] _shadow = new Vector2[ShadowVertices];
    private RayCast2D _ground = null!;
    private Node2D _host = null!;

    /// <summary>纵深从哪个角色读。建节点时给，本节点不缓存纵深值。</summary>
    public required IDepthActor Actor { get; init; }

    /// <summary>找地面的射线往下探多远，世界像素。够穿过一层地形就行。</summary>
    public int GroundProbeWorldPx { get; init; } = 256;

    /// <summary>这一帧影子落在的地面全局 Y。射线什么都没打到时是 <c>null</c>，那时不画影子。</summary>
    public double? GroundYWorldPx { get; private set; }

    /// <summary>这一帧角色离地多高，世界像素。没有地面时是 0。</summary>
    public double HeightAboveGroundWorldPx { get; private set; }

    /// <summary>排序要用的两个量。没有地面时把脚底当成与角色同高。</summary>
    public DepthSubject Subject => new(Actor.DepthWorldPx, GroundYWorldPx ?? _host.GlobalPosition.Y);

    public override void _Ready()
    {
        _host = GetParent<Node2D>();
        _ground = new RayCast2D
        {
            TargetPosition = new Vector2(0, GroundProbeWorldPx),
            // 层 1 是地形层，但主角也在这一层（木桩靠它实现「被击退时撞上主角会停住」）。所以这条
            // 射线只排除宿主自己，另一个角色正好压在宿主正下方时仍会被当成地面。当前场景里碰不到
            // 那种摆法；真要让角色叠在一起站，得先给地形一个专属的碰撞层。
            CollisionMask = 1,
            // 脚底正好贴在地面表面上，射线起点因此落在碰撞体边界上；不许从内部命中就会漏掉那一帧。
            HitFromInside = true,
            Enabled = true,
        };
        // 必须排除宿主自己：不排除的话射线往下第一个打到的就是角色自己的碰撞体，于是「地面」永远
        // 等于角色当前高度、影子永远贴在脚底不动，跳起来看不出高度，而它不报错。挂到宿主之后
        // ExcludeParent 本来就排除了它，这里再显式排一次，好让射线换个挂载点也仍然对。
        if (_host is CollisionObject2D body)
        {
            _ground.AddException(body);
        }
        // 射线挂宿主、不挂本节点：本节点带着纵深的绘制偏移，射线跟着它走就从偏过的位置起算，量出来
        // 的「地面」也带上那次偏移，画影子时又加一次 —— 影子会偏出去整整一个偏移量（实测木桩站在带
        // 最前沿时，影子落到脚底下方 24 像素）。地面是物理量，所以射线得站在宿主的物理位置上问。
        _host.AddChild(_ground);
        Sync();
    }

    /// <summary>按当前纵深与地面刷新绘制偏移和影子。</summary>
    /// <remarks>
    /// 由角色在物理推进之后显式调。顿帧冻结的那几帧不调，于是「冻结期间画面一动不动」连纵深偏移
    /// 与影子一起成立。
    /// </remarks>
    public void Sync()
    {
        Position = new Vector2(0, (float)DepthRendering.DrawOffsetWorldPx(Actor.DepthWorldPx));
        // 位置这一帧刚变过，而射线里存的还是上一帧的结果，所以要强制它重算一次。
        _ground.ForceRaycastUpdate();
        GroundYWorldPx = _ground.IsColliding() ? _ground.GetCollisionPoint().Y : null;
        HeightAboveGroundWorldPx = GroundYWorldPx is double ground
            ? Math.Max(0.0, ground - _host.GlobalPosition.Y)
            : 0.0;
        QueueRedraw();
    }

    /// <summary>影子：一个不透明的扁椭圆，画在角色正下方的地面上，离地越高画得越小。</summary>
    /// <remarks>
    /// 局部 y 取「地面 Y 减角色 Y」：两边各自都带着同一份纵深偏移，相减正好抵掉，所以这里不必
    /// 再管纵深 —— 贴地时得 0（影子与脚底重合），跳起来时得正数（影子留在地面上）。
    ///
    /// 用不透明色而不是半透明，理由见 <see cref="DepthRendering.ShadowScaleAt"/>。
    /// </remarks>
    public override void _Draw()
    {
        if (GroundYWorldPx is not double ground)
        {
            return;
        }

        var scale = DepthRendering.ShadowScaleAt(HeightAboveGroundWorldPx);
        var span = DepthRendering.ShadowSpanAt(Actor.BodySpanWorldPx);
        var radiusX = (float)(span.Width * scale / 2.0);
        var radiusY = (float)(CombatFeel.ShadowHeightWorldPx * scale / 2.0);
        // 横向中心跟着身体的中点走（出拳时会偏向拳那一侧），纵向仍落在地面上。
        var center = new Vector2((float)span.Center, (float)(ground - _host.GlobalPosition.Y));
        for (var i = 0; i < ShadowVertices; i++)
        {
            var angle = Mathf.Tau * i / ShadowVertices;
            _shadow[i] = center + new Vector2(radiusX * Mathf.Cos(angle), radiusY * Mathf.Sin(angle));
        }
        DrawColoredPolygon(_shadow, ShadowColor);
    }
}
