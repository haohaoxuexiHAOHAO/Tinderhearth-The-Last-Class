using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>一段攻击判定框的几何：尺寸（世界像素）与中心离脚底的高度（世界像素，向上为正）。</summary>
public readonly record struct HitboxSpec(Vector2 Size, float CenterY);

/// <summary>
/// Active 硬门后的实时形状查询，不依赖延迟的 Area 重叠缓存。
/// </summary>
/// <remarks>
/// **命中要两个条件都成立**（`GP-16`）：横向由这里的形状查询判，纵深由 <see cref="Connects"/> 补判。
/// 形状查询只覆盖 Godot 2D 的两个轴（横向与跳跃高度），第三个轴不在里面。
/// </remarks>
public partial class Hitbox : Area2D
{
    private readonly HashSet<ulong> _hit = new();
    private readonly RectangleShape2D _shape = new() { Size = SpecFor(ComboKind.Light, 0).Size };
    private bool _wasActive;

    /// <summary>
    /// 上一次 <see cref="Resolve"/> 里通过了两个条件、却被「本次挥击已命中过」驳回的候选数。
    /// </summary>
    /// <remarks>
    /// **这是给判据当量具用的。** 「没打中」有三个来源：横向查询里压根没这个候选、纵深超差、
    /// 本次挥击已经打过它。三者都让 <see cref="Resolve"/> 返回 0，于是一条只看返回值的判据说不清
    /// 自己测到的是哪一个 —— 2026-09-11 实测踩过：探针没让判定框看见非 Active 帧，去重集合是上一
    /// 阶段留下的脏值，「纵深错开打空」那条判据因此假绿。要判「是纵深挡的」，就得同时说明不是它挡的。
    /// </remarks>
    public int RejectedAsAlreadyHit { get; private set; }

    /// <summary>一段攻击判定框的尺寸与中心高度，世界像素。轻击按段（<c>combo.Step</c>）取，重击单招。取值依据见 <see cref="CombatFeel"/>。</summary>
    /// <remarks>
    /// **轻击三段各有一份**（`ART-6`，2026-09-11）：第 1/2/3 段分别对应 <c>light</c>／<c>light2</c>／
    /// <c>light3</c> 精灵表，尺寸与中心都从各自 Active 帧的实测伸展导出（见 <see cref="CombatFeel"/>）。
    /// 踢腿（第 3 段）伸得更远、更低，所以它的框既比拳宽、中心也更靠脚底 —— 三段共用一个框就会
    /// 让画面上那一脚与判定停在两处。重击忽略 <paramref name="step"/>（单招，`HeavyChainLength=1`）。
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
    /// <summary>当前逻辑帧是否开放检测。</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 当前 Active 帧判定框的本地矩形（中心在本节点原点，随 <see cref="Resolve"/> 每帧重定位）；
    /// 非 Active 帧为 <c>null</c>。给 <c>CombatDebugOverlay</c>（`ENG-6`）画框用 —— 读的是与命中
    /// 查询**同一份** <c>_shape</c>，所以叠层画出来的框就是命中真正用的那个，不会各算一份而漂移。
    /// </summary>
    public Rect2? ActiveBoxLocal => IsActive ? new Rect2(-_shape.Size / 2f, _shape.Size) : null;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = false;
        AddChild(new CollisionShape2D { Shape = _shape });
    }

    /// <summary>移动与落地取消完成后调用；每段 Active 起点重置去重集合。</summary>
    /// <remarks>
    /// **调用契约：每个非顿帧物理帧调一次。** 每挥击一个去重集合是靠**相邻两次调用之间**观察到的
    /// 「Active 上升沿」清空的 —— 也就是说它的生命周期挂在调用节奏上，不挂在挥击本身上。稀疏调用
    /// （只在 Active 帧调、跨挥击不调）会让集合停在上一次挥击的内容里，表现是**攻击静默不生效**：
    /// 返回 0、无报错、看起来像判定框或纵深出了问题。顿帧期间不调是安全的，那几帧连段也没推进。
    ///
    /// 手动驱动连段的探针必须自己让本机看见一个非 Active 帧（`HitFeedbackDev` 的
    /// <c>depth-probe-fresh-swing</c> 就是这条的执行体）。想不依赖节奏，得由挥击的拥有者显式告知
    /// 「新挥击开始了」而不是在这里推断 —— 那是改动连段机接口的事，记在设计仓 `issue-GP-16` 的
    /// 遗留里，本轮不做。<see cref="RejectedAsAlreadyHit"/> 让违反这条契约变成查得出来的。
    /// </remarks>
    public int Resolve(PlayerActor player, Action<HitReaction> feedback)
    {
        RejectedAsAlreadyHit = 0;
        IsActive = player.Combat.Combo.IsHitActive;
        if (IsActive && !_wasActive) _hit.Clear();
        _wasActive = IsActive;
        if (!IsActive) return 0;
        // 框按段换尺寸与中心：贴住画面上这一段的拳脚真正伸到的距离与高度（`ART-6` 实测）。轻击第
        // 1/2/3 段各一份，踢腿（第 3 段）更远更低就真的更远更低；重击单招走 Kind、忽略 Step。
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
                || hurt.Actor is not TrainingDummy dummy
                // 查询把这个受击框返回给我们 ＝ 横向那一半成立；纵深那一半在下面这一句里，见 Connects。
                || !Connects(horizontalOverlap: true, player, dummy)) continue;
            // **去重排在纵深判定之后**：打空不是打过。反过来的话，同一次挥击里先错开一排、再挪回
            // 同一排就永远打不中了 —— 而横向那一半本来就是这个口径（框外的候选压根不会出现在查询
            // 结果里，也就不会被登记）。两个轴的口径必须一致，否则纵深会多一条只在特定顺序下才看
            // 得出来的规则，而它不报错。
            if (!_hit.Add(hurt.GetInstanceId()))
            {
                RejectedAsAlreadyHit++;
                continue;
            }
            var reaction = HitResolution.Resolve(player.Combat.Combo.Kind);
            dummy.Receive(reaction, player.Combat.Motor.Facing);
            feedback(reaction);
            count++;
        }
        // 命中回传连段机：轻击续段靠命中确认（`GP-10` 方案 b）。命中检测在这里（形状查询 + 纵深），
        // 规则层拿不到目标,所以由这里通知。任一命中即置位;轻击的续段门用它,重击忽略(仍走时间窗)。
        if (count > 0) player.Combat.Combo.RegisterHit();
        return count;
    }

    /// <summary>命中的两个条件（`GP-16`）：横向重叠**且**双方纵深差在命中容差内。</summary>
    /// <remarks>
    /// 「两个条件都要」的判断只有这一处，理由与代价见 <see cref="DepthOverlap"/>：形状查询答不了
    /// 纵深（Godot 2D 的两个轴被横向与跳跃高度占满，纵深不参与引擎碰撞），所以它必须在拿到候选
    /// **之后**再筛一遍，靠碰撞形状表达不出来。
    ///
    /// **两边的纵深都经 <see cref="IDepthActor"/> 取，这一半由编译器守着。** 正典要求绘制排序与
    /// 命中判定用同一份纵深值（排错了，玩家看到的前后关系会与判定相反），`ENG-15` 已经把那句话落成
    /// 这个接口；参数类型写成接口而不是具体角色类型，本类就**没有第二份纵深可用** —— 想另存一份得
    /// 先改签名，那是改得见的。
    ///
    /// 容差取 <see cref="CombatFeel.HitDepthToleranceWorldPx"/>。`GP-17` 的实体阻挡复用
    /// <see cref="DepthOverlap"/> 但传它自己的阈值，不共用这个数。
    /// </remarks>
    private static bool Connects(bool horizontalOverlap, IDepthActor attacker, IDepthActor target) =>
        DepthOverlap.WithHorizontal(horizontalOverlap, attacker.DepthWorldPx, target.DepthWorldPx,
            CombatFeel.HitDepthToleranceWorldPx);
}
