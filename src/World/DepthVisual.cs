using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// 一个占着纵深的角色。**纵深值只有一份**，实现者从自己的唯一来源转发，不另存一份。
/// </summary>
/// <remarks>
/// 正典要求绘制排序与命中判定用同一份纵深值（排错了，画面上的前后关系会与判定相反）。这个接口
/// 就是那句「同一份」在类型上的落点：排序（`ENG-15`）与将来的命中容差（`GP-16`）都经它取值。
/// </remarks>
public interface IDepthActor
{
    /// <summary>纵深位置，世界像素，口径见 <see cref="DepthBand"/>（0 最靠后）。</summary>
    double DepthWorldPx { get; }

    /// <summary>
    /// 当前姿态贴地的水平**范围**，相对脚底锚点、已含朝向镜像。影子按它取范围。
    /// </summary>
    /// <remarks>
    /// 给范围而不是给宽度：只有宽度的话影子只能对称画在脚底上，出拳那一帧就会与本体错开（见
    /// <see cref="GroundSpan"/>）。由实现者决定「本体」怎么量 —— 有精灵的按当前帧的不透明边界，
    /// 几何体按自己的尺寸。放进接口而不是让影子自己猜，是因为只有角色知道自己此刻是什么姿态。
    /// </remarks>
    GroundSpan BodySpanWorldPx { get; }

    /// <summary>
    /// 排序要用的两个键。实现者转发自己的 <see cref="DepthVisual.Subject"/>，不自己拼。
    /// </summary>
    /// <remarks>
    /// 放进接口而不是让排序层去角色的子节点里找 <see cref="DepthVisual"/>：找的那一步要每帧
    /// 遍历子节点（同屏 20 个角色就是 20 次遍历加分配），而且「找不到就退化成用角色自己的 Y」
    /// 这条兜底分支会**悄悄给出错的排序键** —— 跳跃高度会混进本该只有地面高度的那个键里。
    /// </remarks>
    DepthSubject DepthSubject { get; }
}

/// <summary>
/// 角色的纵深可视根（`ENG-15`）：按纵深偏移画面、画代码影子、报排序要用的量。
/// </summary>
/// <remarks>
/// **角色的可视子节点都挂在这里，不挂在角色本体上。** 这样「纵深偏移」只有一处来源
/// （本节点的 <c>Position</c>），新增一种角色时漏不掉 —— 漏了的表现是那个角色在纵深上走动时
/// 画面不动，而它不报错。角色本体只留物理：<c>Position</c> 仍是横向与跳跃高度，纵深一点都不
/// 掺进去（`GP-15` 的三轴分离）。
///
/// **地面靠射线找，不靠「记住上次在地面的高度」。** 影子要落在角色**下方的地面**上，而地形有
/// 起伏与坑；记忆式的做法在跳过坑时会把影子留在坑沿的高度，那时影子指的位置是错的，而画面上
/// 只是「影子有点怪」。射线每帧问一次真实地形，坑底就是坑底。
/// </remarks>
public partial class DepthVisual : Node2D
{
    private static readonly Color ShadowColor = PixelTheme.ToColor(HudPalette.Charcoal);

    /// <summary>影子椭圆的顶点数。16 个足够圆，且逻辑分辨率下每段都落在整像素边界附近。</summary>
    private const int ShadowVertices = 16;

    private readonly Vector2[] _shadow = new Vector2[ShadowVertices];
    private RayCast2D _ground = null!;
    private Node2D _host = null!;

    /// <summary>纵深的来源。构造时注入，本节点不缓存纵深值。</summary>
    public required IDepthActor Actor { get; init; }

    /// <summary>射线往下探多远，世界像素。够穿过一层地形即可。</summary>
    public int GroundProbeWorldPx { get; init; } = 256;

    /// <summary>本帧影子落在的地面全局 Y；射线没打到东西时为 <c>null</c>（悬崖外，影子不画）。</summary>
    public double? GroundYWorldPx { get; private set; }

    /// <summary>本帧角色离地多高，世界像素。没有地面时为 0。</summary>
    public double HeightAboveGroundWorldPx { get; private set; }

    /// <summary>排序要用的量。没有地面时脚底当作与角色同高。</summary>
    public DepthSubject Subject => new(Actor.DepthWorldPx, GroundYWorldPx ?? _host.GlobalPosition.Y);

    public override void _Ready()
    {
        _host = GetParent<Node2D>();
        _ground = new RayCast2D
        {
            TargetPosition = new Vector2(0, GroundProbeWorldPx),
            // 层 1 是地形层，**但主角也在这一层**（木桩的 `CollisionMask=1` 靠它实现「击退被主角
            // 挡住」，`GP-13` 的 wall-block 盯着那条）。于是本射线只排除宿主自己，另一个角色若
            // 正好压在宿主正下方仍会被当成地面。本轮场景里不会发生（角色都各自站在地面上），
            // 真要多角色叠在一起时得给地形一个专属层 —— 那要动 `GP-13` 的碰撞关系，归 A2 记账。
            CollisionMask = 1,
            // 脚底正好贴在地面表面上，射线起点因此在碰撞体边界上；不允许从内部命中会漏掉那一帧。
            HitFromInside = true,
            Enabled = true,
        };
        // **必须排除宿主自己。** 不排除的话射线从脚底向下第一个命中的是角色自己的碰撞体（主角
        // 就在地形层里），于是「地面」永远等于角色当前高度、影子永远贴在脚底不动 —— 跳起来看不出
        // 高度，而这件事不报错。挂到宿主之后 `ExcludeParent` 已经排除了它，这里再显式排一次是为了
        // 「射线换了挂载点也仍然正确」，不依赖那个默认值。
        if (_host is CollisionObject2D body)
        {
            _ground.AddException(body);
        }
        // **射线挂宿主、不挂本节点。** 本节点带着纵深绘制偏移，射线若跟着它走，起点就已经偏移过
        // 一次：纵深往前（偏移为正）时起点落进地面碰撞体内部，`HitFromInside` 让命中点等于起点，
        // 于是量出来的「地面」也带着那次偏移，最后画影子时又加一次 —— 影子偏出去整整一个偏移量。
        // 2026-09-09 实测过这个形状：木桩在带前沿时影子落在脚底下方 24px。地面是**物理量**，与
        // 绘制偏移无关，所以射线必须站在宿主的物理位置上问。
        _host.AddChild(_ground);
        Sync();
    }

    /// <summary>
    /// 按当前纵深与地面刷新偏移与影子。**由角色在物理推进之后显式调用** —— 顿帧冻结时不调，
    /// 于是「冻结期间画面一动不动」连纵深偏移与影子一起成立。
    /// </summary>
    public void Sync()
    {
        Position = new Vector2(0, (float)DepthRendering.DrawOffsetWorldPx(Actor.DepthWorldPx));
        // 位置这一帧刚变过，射线的缓存结果还是上一帧的，必须强制重算。
        _ground.ForceRaycastUpdate();
        GroundYWorldPx = _ground.IsColliding() ? _ground.GetCollisionPoint().Y : null;
        HeightAboveGroundWorldPx = GroundYWorldPx is double ground
            ? Math.Max(0.0, ground - _host.GlobalPosition.Y)
            : 0.0;
        QueueRedraw();
    }

    /// <summary>
    /// 影子：不透明的扁椭圆，画在地面投影点上，随离地高度缩小。
    /// </summary>
    /// <remarks>
    /// 局部 y 取 <c>地面 Y − 角色 Y</c>：两边各自都带着同一份纵深偏移，相减正好抵消，所以这里
    /// 不必再管纵深 —— 贴地时是 0（影子与脚底重合），跳起来时是正数（影子留在地面）。
    ///
    /// 不透明而不是半透明，理由见 <see cref="DepthRendering.ShadowScaleAt"/>。
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
        // 横向中心跟着本体的中点走（出拳时偏向拳那一侧），纵向仍落在地面投影点上。
        var center = new Vector2((float)span.Center, (float)(ground - _host.GlobalPosition.Y));
        for (var i = 0; i < ShadowVertices; i++)
        {
            var angle = Mathf.Tau * i / ShadowVertices;
            _shadow[i] = center + new Vector2(radiusX * Mathf.Cos(angle), radiusY * Mathf.Sin(angle));
        }
        DrawColoredPolygon(_shadow, ShadowColor);
    }
}
