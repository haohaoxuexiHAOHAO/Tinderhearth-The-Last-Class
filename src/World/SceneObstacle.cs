using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.World;

/// <summary>
/// 场景障碍物（`GP-17`）：占一段纵深、中立、挡住角色，角色只能绕行。
/// </summary>
/// <remarks>
/// 正典把它定成空间模型的一部分：「场景物件在纵深上作阻挡与掩体，玩家与敌人**绕行而不是跳上去**
/// ……它们的职责是让近排与远排之间出现要绕的东西」（设计仓 `canon/gameplay/战斗与关卡.md` 的空间
/// 模型）。绕行成立的前提就是物件真的挡人 —— 那正是本类存在的理由。
///
/// **刻意不叫「巨石」、也不画成石头**：正式关卡里这类东西可能是石头、
/// 树干、翻倒的货架或者别的什么，形状与美术归 `ART-1` 与关卡实现。本类只负责「占一段纵深、挡人」
/// 这一件事，画成一个几何占位。
///
/// **它占的那段纵深不是本类的属性，而是阻挡阈值给的**：本类只有一个纵深值，双方纵深差在
/// <see cref="CombatFeel.BlockDepthThresholdWorldPx"/> 内就挡，于是它实际上占了阈值上下各一段、
/// 合起来约一排。按物件各给一份纵深厚度（大石头该比角色占得多）是更后面的事，见那个常量的说明。
///
/// **静态体、不可受击**：它不会被推走（<see cref="StaticBody2D"/> 就是这句话的类型形式），也没有
/// <see cref="Hurtbox"/> —— 可破坏通路是正典单列的另一件事，现在不做。
/// </remarks>
public partial class SceneObstacle : StaticBody2D, IBlockingActor
{
    /// <summary>
    /// 实体层取 4，与木桩同层、**刻意不进层 1**。
    /// </summary>
    /// <remarks>
    /// 层 1 是地形层，而 <see cref="DepthVisual"/> 的影子射线正是按 <c>Mask=1</c> 找地面的。障碍物
    /// 若进层 1，角色从它旁边（同一个横向位置、另一排）跳过去时，射线会先打到障碍物顶面并把它当成
    /// 地面 —— 影子于是跳到障碍物头顶，而这件事不报错。放层 4 就绕开了整类问题；角色看得见它，是靠
    /// <see cref="DepthBlocker.Add"/> 把这一位补进角色的掩码。
    ///
    /// 层 1 里已经有主角这类角色，所以那条射线的毛病本来就存在（跳过另一个角色时同样会发生），
    /// 那一半归 `ENG-19`。
    /// </remarks>
    private const uint EntityLayer = 4;

    private static readonly Color BodyColor = new("6d6a5c");
    private static readonly Color TopColor = new("8b8778");

    private double _depthWorldPx = DepthBand.CenterWorldPx;

    /// <summary>障碍物宽度，世界像素。碰撞框、绘制与影子范围共用它，不各写一个数（`ENG-6`）。</summary>
    public int WidthWorldPx { get; init; } = 20;

    /// <summary>障碍物高度，世界像素。碰撞框与绘制共用它。</summary>
    public int HeightWorldPx { get; init; } = 24;

    /// <inheritdoc />
    /// <remarks>中立：谁都挡、谁都不帮，见 <see cref="DepthBlocking.Blocks"/> 的三条裁定。</remarks>
    public CombatSide Side => CombatSide.Neutral;

    /// <summary>纵深可视根（`ENG-15`）：几何挂在它下面，纵深偏移与影子都由它管。</summary>
    public DepthVisual Visual { get; private set; } = null!;

    /// <inheritdoc />
    /// <remarks>
    /// 障碍物不建 <see cref="MotorState"/>（不建无用的 Motor，口径同 <see cref="TrainingDummy"/>），
    /// 所以纵深就存在这里，**这是它的唯一一份**：绘制排序（`ENG-15`）与阻挡判定（`GP-17`）读的都是
    /// 它，都经 <see cref="IDepthActor"/>，没有第二份。
    /// </remarks>
    public double DepthWorldPx => _depthWorldPx;

    /// <inheritdoc />
    public DepthSubject DepthSubject => Visual.Subject;

    /// <inheritdoc />
    /// <remarks>几何体没有精灵，本体范围就是它自己那个宽度、以脚底为中心。</remarks>
    public GroundSpan BodySpanWorldPx => GroundSpan.Centered(WidthWorldPx);

    /// <summary>把障碍物摆到带内某个纵深上。带外的值被钳进带内。</summary>
    public void PlaceDepth(double depthWorldPx)
    {
        _depthWorldPx = DepthBand.Clamp(depthWorldPx);
        Visual?.Sync();
    }

    public override void _Ready()
    {
        CollisionLayer = EntityLayer;
        // 静态体自己不移动、不发起查询，所以这里给什么都不影响它 —— 起作用的是**角色**的掩码里有没有
        // 本类这一层，而那一位由 `DepthBlocker.Add` 补。（`Add` 是双向按位或的，所以本行这个 0 之后会被
        // 它加上角色那一层；写 0 只是表明「本类不靠自己的掩码工作」。）
        CollisionMask = 0;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(WidthWorldPx, HeightWorldPx) },
            Position = new Vector2(0, -HeightWorldPx / 2f),
        });
        Visual = new DepthVisual { Actor = this };
        AddChild(Visual);
        // 几何挂在可视根下（`ENG-15`）：纵深偏移只有一处来源，影子天然排在身体下面。
        var half = WidthWorldPx / 2f;
        Visual.AddChild(new Polygon2D
        {
            Polygon = [new(-half, -HeightWorldPx), new(half, -HeightWorldPx), new(half, 0), new(-half, 0)],
            Color = BodyColor,
        });
        // 顶面浅一档：像素风里靠色阶而不是透明度做层次（`ENG-10` 只允许全透明或全不透明）。
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
