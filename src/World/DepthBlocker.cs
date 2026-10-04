using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.World;

/// <summary>一个参与实体阻挡的角色：它站哪一边，以及它在哪个纵深上。</summary>
/// <remarks>
/// 继承 <see cref="IDepthActor"/> 而不是自带一份纵深：阻挡是纵深值的第三个读者（前两个是绘制排序
/// 与命中判定），都走同一个接口，「阻挡用的前后关系」与「画面上的前后关系」就不可能对不上。想另
/// 存一份纵深，得先改这里的签名。
/// </remarks>
public interface IBlockingActor : IDepthActor
{
    /// <summary>此刻站哪一边。说的是当前的敌我关系，不是族群归属，理由见 <see cref="CombatSide"/>。</summary>
    CombatSide Side { get; }

    /// <summary>把它摆到某个纵深上，单位世界像素。</summary>
    /// <remarks>
    /// 引擎碰撞管不到纵深，所以阻挡的纵深那一半要靠这个方法把挤进来的一方停住，
    /// 见 <see cref="DepthBlocker.Resolve"/>。
    /// </remarks>
    void PlaceDepth(double depthWorldPx);
}

/// <summary>
/// 实体阻挡在引擎这一侧的那一半：用碰撞豁免把「此刻不该互相挡的那些对」逐帧关掉。
/// </summary>
/// <remarks>
/// 该不该挡由规则层的 <see cref="DepthBlocking"/> 判，本类只做引擎才能做的事：枚举每一对参与者、
/// 把结论写成碰撞豁免的增删、报出一共覆盖了几个。真正把人挡开的仍是 <c>MoveAndSlide</c>，横向碰撞
/// 引擎已经在算，这里不重算一遍。
///
/// 做法是关掉不该挡的，而不是开启该挡的：Godot 的碰撞层与掩码按类生效，既表达不了纵深，也表达
/// 不了「这一对挡、那一对不挡」—— 同层的实体要么全挡要么全不挡。豁免是 Godot 里唯一按单独一对
/// 生效的开关，所以纵深只能门控它。三个轴怎么分工见 <c>ARCHITECTURE.md</c>。
///
/// 阻挡的口径是「不许走进去」，不是「把已经在里面的推出去」，所以 <see cref="Resolve"/> 里有一条
/// 迟滞；它挡的是哪种事故、实测数字是多少，见 <see cref="OverlapDeferralCount"/>。
/// </remarks>
public sealed class DepthBlocker
{
    /// <summary>一个参与者，连同它实体碰撞框的半宽（从引擎实际在用的那个形状上读来的）。</summary>
    private sealed record Participant(PhysicsBody2D Body, IBlockingActor Actor, double HalfWidthWorldPx)
    {
        /// <summary>上一次 <see cref="Resolve"/> 时它在哪个纵深上，用来判断它是从哪一侧靠近的。</summary>
        public double PreviousDepthWorldPx { get; set; } = Actor.DepthWorldPx;
    }

    private readonly List<Participant> _participants = [];

    /// <summary>此刻被豁免（也就是「不挡」）的那些对。键按实例 id 排序，与登记顺序无关。</summary>
    private readonly HashSet<(ulong Low, ulong High)> _exempt = [];

    /// <summary>登记了几个参与者。场景漏挂一个的表现是它谁都不挡，而那不报错，所以这个数要能查。</summary>
    public int ParticipantCount => _participants.Count;

    /// <summary>上一次 <see cref="Resolve"/> 检查了几对。<c>n</c> 个参与者应当是 <c>n(n−1)/2</c> 对。</summary>
    public int PairCount { get; private set; }

    /// <summary>上一次 <see cref="Resolve"/> 判成「此刻互相阻挡」的有几对。</summary>
    public int BlockingPairCount { get; private set; }

    /// <summary>累计切换过几次豁免状态。纵深与阵营都没变的时候，它一次都不该涨。</summary>
    /// <remarks>
    /// 每帧无条件增删一遍豁免也能得到正确的碰撞行为，所以多余的切换不会表现成任何可见的毛病。
    /// 但把物理服务器的豁免表每帧翻一遍，稳定性是未验证的 —— 本项目此前只在 <c>_Ready</c> 里用过
    /// 一次豁免，没有逐帧切换的先例。所以只在状态真的变了才动它，并把次数暴露出来好核对。
    /// </remarks>
    public int ToggleCount { get; private set; }

    /// <summary>累计有多少次「该挡、但因为两者已经重叠着所以先不挡」。</summary>
    /// <remarks>
    /// 这条迟滞挡的是脱插：先在不同纵深上走到同一个横向位置（此时不挡、可以互相穿过），再往对方那
    /// 一排挪，阻挡就会在两具身体已经叠在一起时才开始生效。实测 Godot 的脱插是一帧到位的 —— 单帧
    /// 位移 17.77 像素（差不多就是整个本体宽 18 像素），而且两具都在做 <c>MoveAndSlide</c> 的角色
    /// 此后会锁步一起漂（实测 60 帧里 X 从 220 漂到 77.8）。画面上就是「莫名被弹开然后一起滑走」。
    ///
    /// 迟滞之后阻挡只在「正好贴上」那一刻开始生效，那时没有可脱的插。代价是：从另一排走进敌人身体
    /// 里的玩家会站在它里面直到自己走出来，而不是被推开 —— 他自己走进去的，所以这个结果读得懂，
    /// 被弹开读不懂。这个数恒为 0 就说明迟滞压根没在工作。
    /// </remarks>
    public int OverlapDeferralCount { get; private set; }

    /// <summary>累计把谁的纵深夹回去过几次。</summary>
    /// <remarks>
    /// 纵深那一半要是接错了线，这个数恒为 0，而毛病的样子是「左右走过去挡住、改用前后走却穿过去」。
    /// 光看豁免那一半看不出这条没在工作。
    /// </remarks>
    public int DepthClampCount { get; private set; }

    /// <summary>登记一个参与者，并把它与已登记者之间的碰撞掩码补成彼此看得见。</summary>
    /// <remarks>
    /// 掩码由本类补，不让场景自己记得配：豁免只能减少碰撞、造不出碰撞，掩码里彼此看不见的两具身体
    /// 加不加豁免都会互相穿过。而且只做按位或、不覆盖掩码 —— 地形那一位必须留着，否则角色会掉下去。
    ///
    /// 参与者必须同时是一具物理体（才加得了豁免）和一个带立场的纵深角色（才判得出该不该挡），
    /// 这两条写在类型参数上，少任何一样都编译不过，而不是等到运行时才发现某个角色没有纵深。
    ///
    /// 没有注销，因为现在还没有会中途消失的参与者。参与者名单持的是强引用，被 <c>QueueFree</c> 掉
    /// 的角色会让 <see cref="Resolve"/> 在访问它时抛出来，而不是静默算错。等敌人会死、会出场退场
    /// 时，该加的是注销，而不是「跳过已释放的」—— 静默跳过会让「忘了注销」变成看不见的事。
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

    /// <summary>实体碰撞框的半宽，世界像素，从引擎实际在用的那个形状上读。</summary>
    /// <remarks>
    /// 不让调用方把宽度再报一遍 —— 报第二遍就多了一处会跟不上的地方。角色的碰撞框宽度本来就来自
    /// <c>PlayerActor.BodyWidthFloorWorldPx</c>，这里读回来的正是那个数。
    ///
    /// 形状不是单个矩形时当场抛，不静默按 0 算。按 0 算的话「已经重叠着」永远判不成立，
    /// <see cref="Resolve"/> 里那条迟滞就永远不生效，而它看起来还在 —— 那比没有它更糟。
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

    /// <summary>重算一遍所有对的阻挡状态。由场景在角色推进之前显式调。</summary>
    /// <remarks>
    /// 因为排在推进之前，它用的是上一帧的纵深，所以结果滞后一帧。纵深每帧最多走 1 世界像素
    /// （<see cref="CombatFeel.DepthSpeedPixelsPerSecond"/> 是 60，每秒 60 个物理帧），相对 8 像素
    /// 的阈值就是 1 像素的误差。消掉它得把角色内部的「算纵深」与「移动」拆成两个回调，不值当。
    ///
    /// 顿帧或帧步进冻结的那几帧不调：没有人移动，豁免表也就不必变。两两枚举在同屏 20 个角色下是
    /// 190 对，每对只做两次减法和一次比较；真嫌慢了该做的是按纵深分桶，不是缓存结论。
    /// </remarks>
    public void Resolve()
    {
        ConstrainDepths();
        UpdateExemptions();
        // 记下这一帧的纵深，下一帧靠它判断谁是从哪一侧靠近的。
        foreach (var participant in _participants)
        {
            participant.PreviousDepthWorldPx = participant.Actor.DepthWorldPx;
        }
    }

    /// <summary>阻挡的纵深那一半：把在纵深上挤进来的一方停在阈值边界上。</summary>
    /// <remarks>
    /// 这一半引擎帮不上忙。横向有 <c>MoveAndSlide</c> 挡，但引擎碰撞只覆盖 Godot 的那两个轴，纵深上
    /// 压根没有碰撞体，物理上拦不住纵深移动。少了这一半的毛病是实机撞出来的：左右走过去被挡住，
    /// 改用前后走却直接穿过 —— 那样「绕行」形同虚设，换一排走到物件跟前再横着挪进它体内就行了。
    ///
    /// 只对横向已经重叠的那些对生效。正好贴住（横向不算重叠）时纵深必须自由，否则玩家走到物件跟前
    /// 就再也挪不动纵深，而那恰恰是绕行要用的那一步。
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
                // 两边各夹一次。没挪动的那一方会原样返回（上一帧与这一帧同深，夹出来就是原位），
                // 所以静止的障碍物不会被对方的移动推走。
                ClampOutOf(a, b);
                ClampOutOf(b, a);
            }
        }
    }

    /// <summary>把 <paramref name="mover"/> 的纵深停在 <paramref name="blocker"/> 的阈值之外。</summary>
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
                // 阵营与纵深两个条件都在规则层判，本类不把其中任何一条再写一遍。
                var wants = DepthBlocking.BlocksAtDepth(a.Actor.Side, b.Actor.Side,
                    a.Actor.DepthWorldPx, b.Actor.DepthWorldPx, CombatFeel.BlockDepthThresholdWorldPx);
                var key = KeyOf(a.Body, b.Body);
                var exempt = _exempt.Contains(key);
                // 迟滞：想挡、但此刻两具身体已经横向重叠着，就先别挡，等它们分开了再挡。
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

    /// <summary>两具实体碰撞框此刻横向重叠着吗。</summary>
    /// <remarks>边界不算重叠，于是正好贴住的那一刻阻挡就能生效。</remarks>
    private static bool HorizontallyOverlapping(Participant a, Participant b) =>
        Math.Abs(a.Body.GlobalPosition.X - b.Body.GlobalPosition.X)
            < a.HalfWidthWorldPx + b.HalfWidthWorldPx;

    /// <summary>单独查一对此刻是不是在互相阻挡（也就是没被豁免）。</summary>
    public bool IsBlocking(PhysicsBody2D a, PhysicsBody2D b) => !_exempt.Contains(KeyOf(a, b));

    /// <summary>只在状态真的变了才动物理服务器的豁免表，理由见 <see cref="ToggleCount"/>。</summary>
    private void Apply(PhysicsBody2D a, PhysicsBody2D b, bool blocks)
    {
        var key = KeyOf(a, b);
        var exempt = _exempt.Contains(key);
        if (blocks != exempt)
        {
            return;     // 该挡且没豁免，或不该挡且已豁免 —— 两种都已经对了，不必动。
        }
        if (blocks)
        {
            a.RemoveCollisionExceptionWith(b);
            b.RemoveCollisionExceptionWith(a);
            _exempt.Remove(key);
        }
        else
        {
            // 两个方向都加：引擎那边是不是对称，本项目没实测过，而加两次只多一次哈希查找。
            a.AddCollisionExceptionWith(b);
            b.AddCollisionExceptionWith(a);
            _exempt.Add(key);
        }
        ToggleCount++;
    }

    /// <summary>一对参与者的键：按实例 id 排序，于是 (a,b) 与 (b,a) 得到同一把键。</summary>
    private static (ulong Low, ulong High) KeyOf(GodotObject a, GodotObject b)
    {
        var idA = a.GetInstanceId();
        var idB = b.GetInstanceId();
        return idA < idB ? (idA, idB) : (idB, idA);
    }
}
