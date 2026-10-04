using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>一段攻击判定框的几何：尺寸（世界像素）与中心离脚底的高度（世界像素，向上为正）。</summary>
public readonly record struct HitboxSpec(Vector2 Size, float CenterY);

/// <summary>攻击的判定框：只在判定窗开着的那几帧，当场向物理世界问一次「框里有谁」。</summary>
/// <remarks>
/// 当场查询，不读 <see cref="Area2D"/> 自己攒的重叠列表 —— 那份列表要等引擎下一次同步才更新，
/// 用它会让命中晚一帧。
///
/// 命中要横向与纵深两个条件都成立：横向由这里的形状查询判，纵深由 <see cref="Connects"/> 补判。
/// 形状查询只覆盖 Godot 的那两个轴（横向与跳跃高度），纵深不在里面。
/// </remarks>
public partial class Hitbox : Area2D
{
    private readonly HashSet<ulong> _hit = new();
    private readonly RectangleShape2D _shape = new() { Size = SpecFor(ComboKind.Light, 0).Size };
    private bool _wasActive;

    /// <summary>
    /// 上一次 <see cref="Resolve"/> 里横向与纵深都过了、却因为「这一挥已经打过它」被驳回的候选数。
    /// </summary>
    /// <remarks>
    /// 「没打中」有三个来源：横向查询里压根没这个候选、纵深差超了容差、这一挥已经打过它。三者都让
    /// <see cref="Resolve"/> 返回 0，所以只看返回值分不清是哪一个。这个计数把第三种单独报出来 ——
    /// 没有它的时候踩过一次：去重集合里留着上一挥的脏值，看起来像是纵深把攻击挡掉了。
    /// </remarks>
    public int RejectedAsAlreadyHit { get; private set; }

    /// <summary>一段攻击判定框的尺寸与中心高度，世界像素。轻击按段取，重击只有一招。</summary>
    /// <remarks>
    /// 轻击三段各有一份，分别对应 <c>light</c>、<c>light2</c>、<c>light3</c> 三张精灵表；尺寸与中心
    /// 高度都是从各自判定帧上量出来的拳脚伸展换算的（数在 <see cref="CombatFeel"/> 里）。第三段是
    /// 踢腿，伸得更远更低，三段共用一个框的话画面上那一脚和判定就会停在两个地方。
    /// </remarks>
    public static HitboxSpec SpecFor(ComboKind kind, int step) => kind == ComboKind.Heavy
        ? new HitboxSpec(new Vector2(CombatFeel.HeavyHitboxWidthWorldPx, CombatFeel.HeavyHitboxHeightWorldPx),
                         CombatFeel.HeavyHitboxCenterYWorldPx)
        : step switch
        {
            0 => new HitboxSpec(new Vector2(CombatFeel.Light1HitboxWidthWorldPx, CombatFeel.Light1HitboxHeightWorldPx),
                                CombatFeel.Light1HitboxCenterYWorldPx),
            1 => new HitboxSpec(new Vector2(CombatFeel.Light2HitboxWidthWorldPx, CombatFeel.Light2HitboxHeightWorldPx),
                                CombatFeel.Light2HitboxCenterYWorldPx),
            _ => new HitboxSpec(new Vector2(CombatFeel.Light3HitboxWidthWorldPx, CombatFeel.Light3HitboxHeightWorldPx),
                                CombatFeel.Light3HitboxCenterYWorldPx),
        };
    /// <summary>这一帧判定窗开着没有。</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 判定窗开着时，判定框的本地矩形（中心在本节点原点）；窗没开时是 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 给调试叠层画框用。它读的是命中查询用的同一个形状，所以叠层画出来的框就是命中真正用的那个，
    /// 不会各算一份然后慢慢对不上。框的位置每帧由 <see cref="Resolve"/> 重摆。
    /// </remarks>
    public Rect2? ActiveBoxLocal => IsActive ? new Rect2(-_shape.Size / 2f, _shape.Size) : null;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = false;
        AddChild(new CollisionShape2D { Shape = _shape });
    }

    /// <summary>结算这一帧的命中，返回打中了几个。在角色移动与落地取消都做完之后调。</summary>
    /// <remarks>
    /// 必须每个物理帧都调一次（顿帧那几帧除外，那时连段也没在推进）。「这一挥打过谁」那个集合是靠
    /// 相邻两次调用之间看到判定窗从关变开来清空的 —— 也就是说它靠的是调用节奏，不是挥击本身。
    /// 只在判定窗开着的帧才调，集合就会留着上一挥的内容，表现是攻击静默不生效：返回 0、不报错、
    /// 看起来像判定框或纵深出了问题。
    ///
    /// 自己推进物理的调用方（比如训练房那条循环）尤其要注意，必须让这里看见一个判定窗关着的帧。
    /// 要彻底不依赖节奏，得让挥击的拥有者显式说一句「新的一挥开始了」，而不是在这里从窗口变化上
    /// 推断 —— 那要改连段机的接口，现在不做。违反这条的样子能从
    /// <see cref="RejectedAsAlreadyHit"/> 看出来。
    /// </remarks>
    public int Resolve(PlayerActor player, Action<HitReaction> feedback)
    {
        RejectedAsAlreadyHit = 0;
        IsActive = player.Combat.Combo.IsHitActive;
        if (IsActive && !_wasActive) _hit.Clear();
        _wasActive = IsActive;
        if (!IsActive) return 0;
        // 框按段换尺寸与中心高度，贴住画面上这一段的拳脚真正伸到的距离与高度（实测量出来的）。
        var spec = SpecFor(player.Combat.Combo.Kind, player.Combat.Combo.Step);
        _shape.Size = spec.Size;
        Position = new Vector2(player.Combat.Motor.Facing * spec.Size.X / 2f, -spec.CenterY);
        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = _shape, Transform = GlobalTransform, CollisionMask = Hurtbox.Layer,
            CollideWithAreas = true, CollideWithBodies = false,
        };
        var count = 0;
        foreach (var item in GetWorld2D().DirectSpaceState.IntersectShape(query))
        {
            if (item["collider"].AsGodotObject() is not Hurtbox hurt || hurt.Actor == player
                // 目标要同时能挨打、也要有纵深。两个类型都写在这里，接新目标时忘了给纵深编译不过。
                || hurt.Actor is not IHittable target || hurt.Actor is not IDepthActor depthTarget
                // 查询把这个受击框返回给我们，就等于横向那一半成立；纵深那一半在下面这一句，见 Connects。
                || !Connects(horizontalOverlap: true, player, depthTarget)) continue;
            // 去重排在纵深判定之后，因为打空不算打过。反过来的话，同一挥里先错开一排、再挪回同一排
            // 就永远打不中了。横向那一半本来就是这个口径（框外的候选压根不会出现在查询结果里），
            // 两个轴口径不一致会多出一条只在特定顺序下才看得出来的规则，而它不报错。
            if (!_hit.Add(hurt.GetInstanceId()))
            {
                RejectedAsAlreadyHit++;
                continue;
            }
            var reaction = HitResolution.Resolve(player.Combat.Combo.Kind);
            target.Receive(reaction, player.Combat.Motor.Facing);
            feedback(reaction);
            count++;
        }
        // 把「这一挥碰到东西了」回传给连段机：轻击要靠它才接得下一段。命中检测在这里，规则层看不见
        // 目标，所以只能由这里通知。打中任意一个就算，重击不看这个标记、仍然按时间窗接。
        if (count > 0) player.Combat.Combo.RegisterHit();
        return count;
    }

    /// <summary>命中的两个条件：横向重叠，而且双方纵深差在命中容差之内。</summary>
    /// <remarks>
    /// 「两个条件都要」只在这一处判。形状查询答不了纵深（Godot 的两个轴被横向与跳跃高度占满了，
    /// 纵深不参与引擎碰撞），所以纵深只能在拿到候选之后再筛一遍，靠碰撞形状表达不出来。
    ///
    /// 两边的纵深都经 <see cref="IDepthActor"/> 取：参数类型写成接口而不是具体的角色类型，这里就
    /// 没有第二份纵深可用，画面上的前后关系与打得着打不着因此不可能对不上。
    ///
    /// 容差取 <see cref="CombatFeel.HitDepthToleranceWorldPx"/>。实体阻挡复用同一个
    /// <see cref="DepthOverlap"/>，但传它自己的阈值 —— 「打得着」的宽容量与「占同一格」的物理量
    /// 不共用一个数。
    /// </remarks>
    private static bool Connects(bool horizontalOverlap, IDepthActor attacker, IDepthActor target) =>
        DepthOverlap.WithHorizontal(horizontalOverlap, attacker.DepthWorldPx, target.DepthWorldPx,
            CombatFeel.HitDepthToleranceWorldPx);
}
