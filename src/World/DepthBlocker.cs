using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.World;

/// <summary>
/// 一个参与实体阻挡的角色（`GP-17`）：它站哪一边，以及它在哪一排。
/// </summary>
/// <remarks>
/// **刻意继承 <see cref="IDepthActor"/> 而不是自带一份纵深。** 正典要求绘制排序与命中判定用同一份
/// 纵深值（排错了，玩家看到的前后关系会与判定相反），`ENG-15` 把那句话落成了那个接口。阻挡是第三个
/// 消费方，走同一个接口，于是「阻挡用的前后关系」与「画面上的前后关系」由构造保证一致 —— 想另存
/// 一份纵深得先改这里的签名，那是改得见的。
/// </remarks>
public interface IBlockingActor : IDepthActor
{
    /// <summary>此刻站哪一边。**是当前敌我关系，不是族群归属**，理由见 <see cref="CombatSide"/>。</summary>
    CombatSide Side { get; }

    /// <summary>
    /// 把它摆到某个纵深上。阻挡的纵深那一半靠它把挤进来的一方停住
    /// （<see cref="DepthBlocker.Resolve"/>），因为引擎碰撞管不到纵深。
    /// </summary>
    void PlaceDepth(double depthWorldPx);
}

/// <summary>
/// 带纵深的实体阻挡的引擎那一半（`GP-17`）：**用碰撞豁免把「不该挡的那些对」逐帧关掉。**
/// </summary>
/// <remarks>
/// 判定在规则层（<see cref="DepthBlocking"/>），本类只做引擎的三件事：找出每一对参与者、把结论落成
/// 碰撞豁免的增删、报覆盖量。真正的挡开仍是 <c>MoveAndSlide</c> 干的 —— 横向的碰撞形状引擎已经在
/// 算，规则层不重算（`ENG-6` 那条「读同一份几何、不自己重算」）。
///
/// **为什么是「关掉不该挡的」而不是「开启该挡的」**：Godot 的碰撞是按层与掩码开的，那两个量表达不了
/// 纵深（`ARCHITECTURE.md` 的「战斗：三个轴」），也表达不了「这一对挡、那一对不挡」—— 同层的实体
/// 要么全挡要么全不挡。豁免是唯一按**对**生效的开关，所以纵深门控只能落在它上面。
///
/// **参与者之间的掩码由本类补齐**（<see cref="Add"/>）：豁免只能减少碰撞、不能凭空造出碰撞，掩码
/// 里彼此看不见的两具身体加不加豁免都会互相穿过。让场景自己记得配掩码是一处能忘的注册，忘了的表现
/// 正是本条要修的那件事 —— 该挡的没挡，而且不报错。
///
/// **阻挡是「不许走进去」，不是「把已经在里面的推出去」。** 这条不是洁癖，是实测逼出来的：先在不同
/// 排上走到同一个横向位置（此时不挡、可以互相穿过），再往对方那一排挪 —— 阻挡会在两具身体已经叠在
/// 一起时才开始生效，而实测 Godot 的脱插是**一帧到位**的：单帧位移
/// 17.77px（约等于整个本体宽 18px），而且两具都在做 <c>MoveAndSlide</c> 的角色此后会**锁步一起漂**
/// （实测 60 帧从 X=220 漂到 X=77.8）。画面上那就是「莫名被弹开然后一起滑走」。
///
/// 所以 <see cref="Resolve"/> 里有一条迟滞：**想挡、但此刻两者已经横向重叠着，就先不挡**，等它们分开
/// 了再挡（<see cref="OverlapDeferralCount"/> 自报它生效过几次）。于是脱插压根不会发生 —— 阻挡只在
/// 「正好贴上」那一刻开始生效，而那时没有可脱的插。代价写明：从另一排走进敌人身体里的玩家会站在它
/// 里面直到自己走出来，而不是被推开。那是可读的（他自己走进去的），被弹开不是。
///
/// **没有注销，因为现在没有会消失的参与者。** 登记表持强引用，被 <c>QueueFree</c> 掉的角色会让
/// <see cref="Resolve"/> 在访问它时抛，而不是静默算错 —— 那是刻意的：敌人死亡与出场退场归后续玩法
/// 需求，届时该加的是注销，而不是「跳过已释放的」（静默跳过会让「忘了注销」变成看不见的事）。
///
/// **一帧的滞后写明**：<see cref="Resolve"/> 由场景在角色推进**之前**调，所以它用的是上一帧的纵深。
/// 60Hz 下纵深每帧最多走 1 世界像素（<see cref="CombatFeel.DepthSpeedPixelsPerSecond"/>＝60），
/// 相对 8px 的阈值是一帧 1px 的误差。要消掉它得把「算纵深」与「移动」在角色内部拆开分别回调，那是
/// 改 <c>AdvanceCombat</c> 接口的事，收益是 1px、代价是每个角色多一个调用契约，不值当。
/// </remarks>
public sealed class DepthBlocker
{
    /// <summary>一个参与者，连同它实体碰撞框的半宽（从引擎实际在用的形状上读来的）。</summary>
    private sealed record Participant(PhysicsBody2D Body, IBlockingActor Actor, double HalfWidthWorldPx)
    {
        /// <summary>上一次 <see cref="Resolve"/> 时它在哪个纵深上。用来判断它从哪一侧靠近。</summary>
        public double PreviousDepthWorldPx { get; set; } = Actor.DepthWorldPx;
    }

    private readonly List<Participant> _participants = [];

    /// <summary>此刻处于豁免状态（也就是「不挡」）的对。键按实例 id 排序，与登记顺序无关。</summary>
    private readonly HashSet<(ulong Low, ulong High)> _exempt = [];

    /// <summary>登记了几个参与者。**自报覆盖量**：漏挂一个的表现是它谁都不挡，而那不报错。</summary>
    public int ParticipantCount => _participants.Count;

    /// <summary>上一次 <see cref="Resolve"/> 检查了几对。<c>n</c> 个参与者应当是 <c>n(n−1)/2</c> 对。</summary>
    public int PairCount { get; private set; }

    /// <summary>上一次 <see cref="Resolve"/> 判定为「此刻互相阻挡」的对数。</summary>
    public int BlockingPairCount { get; private set; }

    /// <summary>
    /// 累计切换了几次豁免状态。**给判据当量具**：纵深与阵营都没变时它必须一次都不涨。
    /// </summary>
    /// <remarks>
    /// 每帧无条件 <c>Add</c>／<c>Remove</c> 一遍也能得到正确的碰撞行为，所以「有没有做无谓的切换」
    /// 不会被任何行为判据报出来。而它不是洁癖问题：把物理服务器的豁免表每帧翻一遍，是**未验证**的
    /// 稳定性风险（本项目此前只在 <c>_Ready</c> 里用过一次豁免，没有逐帧切换的先例）。所以本类只在
    /// 状态真的变了才动它，并把切换次数暴露出来让探针钉住这件事。
    /// </remarks>
    public int ToggleCount { get; private set; }

    /// <summary>
    /// 累计有多少次「该挡、但因为两者已经重叠着而先不挡」。**自报覆盖量**：这条迟滞若从没生效过，
    /// 它恒为 0，于是「护栏静默失效」查得出来。
    /// </summary>
    public int OverlapDeferralCount { get; private set; }

    /// <summary>
    /// 累计把谁的纵深夹回过几次。**自报覆盖量**：纵深那一半若接错线它恒为 0，而缺陷形状是「左右走挡住、
    /// W／S 走却穿过去」—— 光看豁免那一半的判据全绿，看不出这条没在工作。
    /// </summary>
    public int DepthClampCount { get; private set; }

    /// <summary>
    /// 登记一个参与者，并把它与已登记者之间的碰撞掩码补成彼此可见。
    /// </summary>
    /// <remarks>
    /// 约束写在类型上：参与者必须**同时**是一具物理体（才加得了豁免）与一个带立场的纵深角色（才判
    /// 得出该不该挡）。少任何一样都编译不过，而不是运行时才发现某个角色没有纵深。
    ///
    /// **只做按位或，不覆盖掩码**：地形那一位必须留着，否则角色会掉下去 —— 那是「顺手改掩码」最容易
    /// 造出来的事故。
    /// </remarks>
    public void Add<T>(T actor) where T : PhysicsBody2D, IBlockingActor
    {
        foreach (var participant in _participants)
        {
            participant.Body.CollisionMask |= actor.CollisionLayer;
            actor.CollisionMask |= participant.Body.CollisionLayer;
        }
        _participants.Add(new Participant(actor, actor, HalfWidthOf(actor)));
    }

    /// <summary>
    /// 实体碰撞框的半宽，**从引擎实际在用的那个形状上读**。
    /// </summary>
    /// <remarks>
    /// 不让调用方把宽度再报一遍：报第二遍就多了一处会漂的地方（`ENG-6` 那条「读同一份几何、不自己
    /// 重算」）。角色的碰撞框宽度本来就来自 <c>PlayerActor.BodyWidthFloorWorldPx</c>，这里读回来的正是
    /// 那个数。
    ///
    /// **假设不成立时当场抛，不静默按 0 算。** 按 0 算的表现是「已经重叠着」永远判不成立，于是
    /// <see cref="Resolve"/> 里那条迟滞永远不生效 —— 而那正是本类要防的缺陷（挡在已重叠时生效会把人
    /// 弹开）。一个静默失效的护栏比没有护栏更糟，因为它看起来是有的。
    /// </remarks>
    private static double HalfWidthOf(PhysicsBody2D body)
    {
        var owners = body.GetShapeOwners();
        if (owners.Length != 1)
        {
            throw new InvalidOperationException(
                $"{body.Name} 有 {owners.Length} 个碰撞形状持有者，实体阻挡只支持单个矩形碰撞框");
        }
        var owner = (uint)owners[0];
        if (body.ShapeOwnerGetShapeCount(owner) != 1
            || body.ShapeOwnerGetShape(owner, 0) is not RectangleShape2D rect)
        {
            throw new InvalidOperationException(
                $"{body.Name} 的碰撞形状不是单个 RectangleShape2D，实体阻挡读不出它的半宽");
        }
        return rect.Size.X / 2.0;
    }

    /// <summary>
    /// 重算一遍所有对的阻挡状态。**由场景在角色推进之前显式调用**，滞后见类型说明。
    /// </summary>
    /// <remarks>
    /// 顿帧或帧步进冻结时不调：那几帧没有人移动，豁免表也就不必变。<c>n²</c> 的对枚举在同屏 20 个
    /// 角色下是 190 对，每对只做两次减法与一次比较；真到嫌慢时该做的是按纵深分桶，不是缓存结论。
    /// </remarks>
    public void Resolve()
    {
        ConstrainDepths();
        UpdateExemptions();
        // 记下这一帧的纵深，下一帧靠它判断谁从哪一侧靠近。
        foreach (var participant in _participants)
        {
            participant.PreviousDepthWorldPx = participant.Actor.DepthWorldPx;
        }
    }

    /// <summary>
    /// 阻挡的**纵深那一半**：把在纵深上挤进来的一方停在阈值边界上（`GP-17`，实机发现的洞）。
    /// </summary>
    /// <remarks>
    /// **这一半不能交给引擎。** 横向由 <c>MoveAndSlide</c> 挡，但引擎碰撞只覆盖 Godot 的两个轴，纵深
    /// 没有碰撞体，所以引擎物理上拦不住纵深移动。少了这一半，左右走过去被挡住、改用 W／S 在纵深上走
    /// 进去却直接穿过 —— 「绕行」形同虚设，因为可以换一排走到物件跟前再挪进它体内。
    ///
    /// **只对横向重叠的对生效**：正好贴住（横向不算重叠）时纵深必须自由，否则玩家走到物件跟前就再也
    /// 挪不动纵深，而那恰恰是「绕行」要用的那一步。
    ///
    /// **滞后一帧、最多插进 1px**：本方法在角色推进**之前**跑，纠正的是上一帧的纵深移动，而纵深每帧
    /// 最多走 1 世界像素（<see cref="CombatFeel.DepthSpeedPixelsPerSecond"/>＝60，60Hz）。这与豁免那一半
    /// 同一个滞后口径，好过为它多开一个必须记得调的第二调用点 —— 忘了调的表现正是本条要修的缺陷。
    /// </remarks>
    private void ConstrainDepths()
    {
        for (var i = 0; i < _participants.Count; i++)
        {
            for (var j = i + 1; j < _participants.Count; j++)
            {
                var a = _participants[i];
                var b = _participants[j];
                if (!DepthBlocking.Blocks(a.Actor.Side, b.Actor.Side) || !HorizontallyOverlapping(a, b))
                {
                    continue;
                }
                // 两边各夹一次。**没挪动的那一方会原样返回**（上一帧与这一帧同深，夹逼算出来就是原位），
                // 所以静止的障碍物不会被对方的移动推走。
                ClampOutOf(a, b);
                ClampOutOf(b, a);
            }
        }
    }

    /// <summary>把 <paramref name="mover"/> 停在 <paramref name="blocker"/> 的阈值之外。</summary>
    private void ClampOutOf(Participant mover, Participant blocker)
    {
        var allowed = DepthBlocking.ClampDepthOutOf(mover.Actor.DepthWorldPx,
            blocker.Actor.DepthWorldPx, mover.PreviousDepthWorldPx,
            CombatFeel.BlockDepthThresholdWorldPx);
        if (Math.Abs(allowed - mover.Actor.DepthWorldPx) <= 1e-9)
        {
            return;
        }
        mover.Actor.PlaceDepth(allowed);
        DepthClampCount++;
    }

    /// <summary>豁免那一半：把「不该挡的对」逐帧关掉。</summary>
    private void UpdateExemptions()
    {
        PairCount = 0;
        BlockingPairCount = 0;
        for (var i = 0; i < _participants.Count; i++)
        {
            for (var j = i + 1; j < _participants.Count; j++)
            {
                var a = _participants[i];
                var b = _participants[j];
                PairCount++;
                // 阵营与纵深两个条件都在规则层判，本类不复述任何一条。
                var wants = DepthBlocking.BlocksAtDepth(a.Actor.Side, b.Actor.Side,
                    a.Actor.DepthWorldPx, b.Actor.DepthWorldPx, CombatFeel.BlockDepthThresholdWorldPx);
                var key = KeyOf(a.Body, b.Body);
                var exempt = _exempt.Contains(key);
                // 迟滞：想挡、但此刻两具身体**已经横向重叠着**，就先别挡 —— 等它们分开了再挡。
                var deferred = wants && exempt && HorizontallyOverlapping(a, b);
                if (deferred)
                {
                    OverlapDeferralCount++;
                }
                var blocks = wants && !deferred;
                if (blocks)
                {
                    BlockingPairCount++;
                }
                Apply(a.Body, b.Body, blocks);
            }
        }
    }

    /// <summary>
    /// 两具实体碰撞框此刻横向重叠着吗。**边界不算重叠**，于是正好贴住的那一刻阻挡就能生效。
    /// </summary>
    private static bool HorizontallyOverlapping(Participant a, Participant b) =>
        Math.Abs(a.Body.GlobalPosition.X - b.Body.GlobalPosition.X)
            < a.HalfWidthWorldPx + b.HalfWidthWorldPx;

    /// <summary>这一对此刻是不是被判为互相阻挡（也就是没有豁免）。给探针查单独一对用。</summary>
    public bool IsBlocking(PhysicsBody2D a, PhysicsBody2D b) => !_exempt.Contains(KeyOf(a, b));

    /// <summary>只在状态真的变了才动物理服务器的豁免表，理由见 <see cref="ToggleCount"/>。</summary>
    private void Apply(PhysicsBody2D a, PhysicsBody2D b, bool blocks)
    {
        var key = KeyOf(a, b);
        var exempt = _exempt.Contains(key);
        if (blocks != exempt)
        {
            return;     // 该挡且没豁免，或不该挡且已豁免 —— 两种都已经是对的，不必动。
        }
        if (blocks)
        {
            a.RemoveCollisionExceptionWith(b);
            b.RemoveCollisionExceptionWith(a);
            _exempt.Remove(key);
        }
        else
        {
            // **两个方向都加。** 引擎那边是不是对称本项目没有实测依据，加两次的代价是一次哈希查找。
            a.AddCollisionExceptionWith(b);
            b.AddCollisionExceptionWith(a);
            _exempt.Add(key);
        }
        ToggleCount++;
    }

    /// <summary>对的键：按实例 id 排序，于是 (a,b) 与 (b,a) 是同一把键。</summary>
    private static (ulong Low, ulong High) KeyOf(GodotObject a, GodotObject b)
    {
        var idA = a.GetInstanceId();
        var idB = b.GetInstanceId();
        return idA < idB ? (idA, idB) : (idB, idA);
    }
}
