using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.World;

/// <summary>
/// 场景障碍物：占着一段纵深、谁都挡，角色只能绕过去。现在画成一个几何占位。
/// </summary>
/// <remarks>
/// 它为什么存在：关卡要让近处与远处之间有要绕的东西，而绕行成立的前提是物件真的挡人。
/// 规则出自设计仓 canon/gameplay/战斗与关卡.md 的「战斗关卡的空间模型：带纵深的横版」一节。
///
/// 刻意不叫「巨石」、也不画成石头：正式关卡里这类东西可能是石头、树干、翻倒的货架或别的什么，
/// 长什么样归关卡与美术。本类只管「占一段纵深、挡人」这一件事；它也没有受击区域，打不坏 ——
/// 可破坏的通路是另一件事，现在不做。
///
/// 它占多厚的纵深不是本类的属性，是阻挡阈值给的：本类只有一个纵深值，双方纵深差在
/// <see cref="CombatFeel.BlockDepthThresholdWorldPx"/> 之内就挡，于是它实际占了阈值上下各一段。
/// 按物件各给一份纵深厚度（大石头该比角色占得多）是更后面的事，见那个常量的说明。
/// </remarks>
public partial class SceneObstacle : StaticBody2D, IBlockingActor
{
    /// <summary>障碍物待的碰撞层，取 4。与木桩同层，刻意不进层 1。</summary>
    /// <remarks>
    /// 层 1 是地形层，而 <see cref="DepthVisual"/> 找地面的那条射线正是只看层 1。障碍物要是进了
    /// 层 1，角色在它旁边（同一个横向位置、另一个纵深）跳过去时，射线会先打到障碍物顶面并把它当成
    /// 地面，影子就跳到障碍物头顶去了，而它不报错。放层 4 就绕开了这一整类问题。
    ///
    /// 角色能看见它，靠的是 <see cref="DepthBlocker.Add"/> 把这一位补进角色的掩码。顺带说明：层 1
    /// 里本来就有主角这类角色，所以那条射线的毛病在「跳过另一个角色」时仍然存在，那一半另说。
    /// </remarks>
    private const uint EntityLayer = 4;

    private static readonly Color BodyColor = new("6d6a5c");
    private static readonly Color TopColor = new("8b8778");

    private double _depthWorldPx = DepthBand.CenterWorldPx;

    /// <summary>障碍物宽度，世界像素。碰撞框、绘制与影子范围共用它，不各写一个数。</summary>
    public int WidthWorldPx { get; init; } = 20;

    /// <summary>障碍物高度，世界像素。碰撞框与绘制共用它。</summary>
    public int HeightWorldPx { get; init; } = 24;

    /// <inheritdoc />
    /// <remarks>中立：谁都挡、谁都不帮，见 <see cref="DepthBlocking.Blocks"/>。</remarks>
    public CombatSide Side => CombatSide.Neutral;

    /// <summary>纵深可视根：几何挂在它下面，纵深偏移与影子都由它管。</summary>
    public DepthVisual Visual { get; private set; } = null!;

    /// <inheritdoc />
    /// <remarks>
    /// 障碍物不动，所以不给它建一套运动状态，纵深就存在本类的字段里 —— 这是它唯一的一份。
    /// 绘制排序与阻挡判定读的都是它，都经 <see cref="IDepthActor"/>。
    /// </remarks>
    public double DepthWorldPx => _depthWorldPx;

    /// <inheritdoc />
    public DepthSubject DepthSubject => Visual.Subject;

    /// <inheritdoc />
    /// <remarks>它是几何体、没有精灵，所以贴地范围就是自己那个宽度、以脚底为中心。</remarks>
    public GroundSpan BodySpanWorldPx => GroundSpan.Centered(WidthWorldPx);

    /// <summary>把障碍物摆到某个纵深上。超出可行走纵深范围的值会被钳回范围内。</summary>
    public void PlaceDepth(double depthWorldPx)
    {
        _depthWorldPx = DepthBand.Clamp(depthWorldPx);
        Visual?.Sync();
    }

    public override void _Ready()
    {
        CollisionLayer = EntityLayer;
        // 静态体自己不移动、不发起查询，所以这里填什么都不影响它。真正起作用的是角色的掩码里有没有
        // 本类这一层，那一位由 DepthBlocker.Add 补上（它是双向按位或的，所以这个 0 之后会被它加上
        // 角色那一层）。写 0 只是表明本类不靠自己的掩码工作。
        CollisionMask = 0;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(WidthWorldPx, HeightWorldPx) },
            Position = new Vector2(0, -HeightWorldPx / 2f),
        });
        Visual = new DepthVisual { Actor = this };
        AddChild(Visual);
        // 几何挂在可视根下面：纵深偏移因此只有一处来源，而且影子天然排在身体下面。
        var half = WidthWorldPx / 2f;
        Visual.AddChild(new Polygon2D
        {
            Polygon = [new(-half, -HeightWorldPx), new(half, -HeightWorldPx), new(half, 0), new(-half, 0)],
            Color = BodyColor,
        });
        // 顶面用浅一档的颜色：像素风里靠色阶做层次，不靠半透明叠色。
        Visual.AddChild(new Polygon2D
        {
            Polygon =
            [
                new(-half, -HeightWorldPx), new(half, -HeightWorldPx),
                new(half, -HeightWorldPx + 4), new(-half, -HeightWorldPx + 4),
            ],
            Color = TopColor,
        });
        Visual.Sync();
    }
}
