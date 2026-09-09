using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.World;

/// <summary>GP-12 主角节点，位置归物理引擎、动作与速度归规则层。</summary>
public partial class PlayerActor : CharacterBody2D, IDepthActor
{
    // ── 精灵表几何（`ART-6`）───────────────────────────────────────────
    // 三个数由 tools/import_role_sheets.py 从收件箱原件量出来，同时写进 tools/asset-registry.json
    // 的「自绘素材」一节（帧宽／帧高／帧内锚点列／帧内地面行四项）。**这里不重新推导，只做一致性
    // 校验**：载入时按纹理实测尺寸核（高必须等于帧高、宽必须是帧宽的整数倍），对不上就当缺图走
    // 占位分支。于是「表重新生成后帧框变了而代码没跟着改」不会静默错位 —— 它会当场退回占位并打日志。
    //
    // 但纹理自校验有个洞：**它管得住宽高，管不住地面行**。GroundRow 只影响精灵往上抬多少，改错
    // 一像素的表现是「脚底离地」或「陷进地面」，纹理尺寸照样对得上、日志照样干净。所以这三个数
    // 与登记表的相等由守卫盯着：`tools/check_assets.py` 的 `check_frame_geometry_binding`
    // 逐条比对，两个方向都撞过真实缺陷形状（`tools/selfcheck_verify.py`）。
    //
    // 为什么不让引擎直接读登记表、彻底免掉这份重复：`tools/` 带 `.gdignore`，登记表不进发行包，
    // 导出后的产物在运行时拿不到它。把它搬进 `res://data/` 能读，但那等于把一份**开发期量具**
    // 变成随发行分发的内容数据，还要跟着 mod 与联机的外置口径走 —— 代价比一条守卫大。
    // 三个数对开发探针可见（`internal`）：`PlayerDev` 拿它们逐像素核脚底行与本体高度，
    // 而不是在探针里再抄一遍数字 —— 抄第二遍就又多了一处会漂的地方。
    internal const int FrameWidth = 44;
    internal const int FrameHeight = 32;

    /// <summary>帧内地面行：脚底像素落在它上一行，精灵偏移就是把这一行对到节点原点。</summary>
    internal const int GroundRow = 30;

    private const string SheetDir = "res://assets/self-drawn/test-role";

    // 本轮接进 A1 玩法的动作。另外六张已入仓（登记表里有）但**刻意不载入**：`light_hit`、
    // `heavy_hit`、`general_defense`、`precise_defense`、`imbalance`、`death` —— 受击、防御、
    // 失衡与死亡的玩法都还没有，载进来只会出现「有图没规则」的半成品状态。
    // `imbalance` 另有一层不确定：失衡与失衡恢复合在同一张 5 帧表里，帧号区间**作者尚未给出**，
    // 就算现在想接也没有可依据的切分点。
    private static readonly string[] Sheets = ["idle", "walk", "run", "jump", "dodge", "light", "heavy"];

    // 攻击相位 → 精灵帧的**占位映射**（`ART-6` 未收口）。轻击 6 帧被 3 段连段共用、重击 7 帧被
    // 2 段共用，所以每段看起来一样 —— 分辨不出第几段要三倍的帧数，那是正式角色美术的事。
    // 唯一的硬约束在这里：**前摇只准用第 0 帧**。作者的第 1 帧是静止起手姿、第 2 帧就是拳伸到
    // 最远的命中姿，若让渲染时钟自己跑，命中姿会在判定框开之前先亮出来，玩家学到的时机是错的。
    private const int AttackStartupFrame = 0;
    internal const int AttackActiveFirstFrame = 1;
    internal const int AttackActiveSpan = 2;

    public string ActorId { get; init; } = "player";
    public ActorControllerRegistry Controllers { get; init; } = new();
    public ActorCombatState Combat { get; } = new();
    public AnimatedSprite2D Sprite { get; } = new();
    public string VisualAction { get; private set; } = "idle";
    private int _visualFrame;

    /// <summary>
    /// 纵深可视根（`ENG-15`）：精灵挂在它下面，纵深偏移与影子都由它管。
    /// </summary>
    public DepthVisual Visual { get; private set; } = null!;

    /// <inheritdoc />
    /// <remarks>转发规则层那一份，不另存 —— 纵深的唯一来源是 <c>Combat.Motor</c>。</remarks>
    public double DepthWorldPx => Combat.Motor.DepthWorldPx;

    /// <inheritdoc />
    public DepthSubject DepthSubject => Visual.Subject;

    /// <summary>缺图退回几何占位的动作名，开发探针用；空表示全部动作都有真图。</summary>
    public IReadOnlyList<string> MissingSheets => _missing;
    private readonly List<string> _missing = [];

    public override void _Ready()
    {
        if (Engine.PhysicsTicksPerSecond != CombatFeel.PhysicsTicksPerSecond)
        {
            throw new InvalidOperationException("Combat requires 60 physics ticks per second");
        }
        _ = Controllers.Require(ActorId);
        // 碰撞框贴角色实测轮廓：站立姿 28px 高（登记表「角色本体」那一项，去掉地面参考线后量的），
        // 底边落在节点原点 —— 原点即脚底，精灵偏移也对到同一处。
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(18, 28) },
            Position = new Vector2(0, -14),
        });
        Sprite.SpriteFrames = new SpriteFrames();
        // 把帧内地面行对到节点原点：帧居中绘制，行 r 的上沿在精灵局部 y = r - 帧高/2。
        Sprite.Position = new Vector2(0, -(GroundRow - FrameHeight / 2));
        foreach (var name in Sheets)
        {
            var texture = GD.Load<Texture2D>($"{SheetDir}/{name}.png");
            if (texture == null || texture.GetHeight() != FrameHeight
                || texture.GetWidth() % FrameWidth != 0 || texture.GetWidth() == 0)
            {
                GD.PushWarning($"[GP12] Missing or invalid sheet: {name}");
                _missing.Add(name);
                continue;
            }
            var count = texture.GetWidth() / FrameWidth;
            Sprite.SpriteFrames.AddAnimation(name);
            for (var frame = 0; frame < count; frame++)
            {
                Sprite.SpriteFrames.AddFrame(name, new AtlasTexture
                {
                    Atlas = texture,
                    Region = new Rect2(frame * FrameWidth, 0, FrameWidth, FrameHeight),
                });
            }
            GD.Print($"[GP12] Sheet {name}={count}");
        }
        GD.Print(_missing.Count == 0
            ? $"[GP12] Missing animations: none; frame {FrameWidth}x{FrameHeight} ground row {GroundRow}"
            : $"[GP12] Missing animations: {string.Join(", ", _missing)}; geometry fallback");
        // 精灵挂在纵深可视根下，于是纵深偏移只有一处来源（`ENG-15`）。
        Visual = new DepthVisual { Actor = this };
        AddChild(Visual);
        Visual.AddChild(Sprite);
        UpdateVisual();
    }

    /// <summary>由战斗场景统一推进时关闭默认物理回调。</summary>
    public bool ManualPhysics { get; init; }

    public override void _PhysicsProcess(double delta)
    {
        if (!ManualPhysics) AdvanceCombat();
    }

    /// <summary>推进一个未冻结的物理帧，包括碰撞后取消与动画。</summary>
    public void AdvanceCombat()
    {
        var controller = Controllers.Require(ActorId);
        var input = controller is ICombatController combatController
            ? combatController.ReadCombatInput(new ActorView(ActorId)) : CombatInput.None;
        Combat.Tick(input, IsOnFloor());
        Velocity = new Vector2((float)Combat.Motor.HorizontalVelocity, (float)Combat.Motor.VerticalVelocity);
        MoveAndSlide();
        Combat.AfterMove(IsOnFloor(), IsOnCeiling(), IsOnWall(), Velocity.X);
        // 位置已定才刷纵深偏移与影子：射线要问的是这一帧的最终位置。
        Visual.Sync();
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        var motor = Combat.Motor;
        var combo = Combat.Combo;
        var action = combo.IsAttacking
            ? combo.Kind == ComboKind.Heavy ? "heavy" : "light"
            : motor.Phase == MotorPhase.Dodge ? "dodge"
            : motor.Phase == MotorPhase.Airborne ? "jump"
            : motor.Phase == MotorPhase.Dash ? "run"
            // **两个轴都算「在走」**（`ENG-15` 修）。原来只看横向速度，于是纯纵深移动时动作是
            // idle —— 角色站着不动地在纵深上滑，而这件事不报错：位置在变、判据全绿、只有眼睛
            // 看得出来。走纵深复用侧面行走姿态，不需要新素材：belt-scroll 那一类作品都是这么
            // 做的（没有「往里走」的专用动画），而正典也定了角色不因远近缩放、朝向只有左右两面。
            : Math.Abs(motor.HorizontalVelocity) > 0 || Math.Abs(motor.DepthVelocity) > 0
                ? "walk" : "idle";
        _visualFrame = action == VisualAction ? _visualFrame + 1 : 0;
        VisualAction = action;
        Sprite.Visible = Sprite.SpriteFrames.HasAnimation(action);
        Sprite.FlipH = motor.Facing < 0;
        Sprite.Modulate = motor.IsInvulnerable ? Colors.Cyan : motor.Phase == MotorPhase.Dash ? Colors.Yellow : Colors.White;
        if (Sprite.Visible)
        {
            Sprite.Animation = action;
            var count = Sprite.SpriteFrames.GetFrameCount(action);
            // 攻击与闪避按**规则层相位**取帧，不许渲染时钟自己跑；其余动作才用渲染计数循环。
            Sprite.Frame = combo.IsAttacking ? AttackFrame(combo, count)
                : motor.Phase == MotorPhase.Dodge ? PhaseFrame(_visualFrame, CombatFeel.DodgeDurationFrames, count)
                : action == "jump" ? Math.Min(count - 1, _visualFrame / LoopTicks(action))
                : (_visualFrame / LoopTicks(action)) % count;
        }
        QueueRedraw();
    }

    /// <summary>一段攻击的相位 → 精灵帧。命中姿绝不出现在前摇里，见类顶部注释。</summary>
    private static int AttackFrame(ComboStateMachine combo, int count)
    {
        var heavy = combo.Kind == ComboKind.Heavy;
        var recoveryFirst = AttackActiveFirstFrame + AttackActiveSpan;
        return combo.Phase switch
        {
            AttackPhase.Startup => AttackStartupFrame,
            AttackPhase.Active => AttackActiveFirstFrame + PhaseFrame(combo.FrameInPhase,
                heavy ? CombatFeel.HeavyActiveFrames : CombatFeel.LightActiveFrames, AttackActiveSpan),
            _ => recoveryFirst + PhaseFrame(combo.FrameInPhase,
                heavy ? CombatFeel.HeavyRecoveryFrames : CombatFeel.LightRecoveryFrames,
                count - recoveryFirst),
        };
    }

    /// <summary>把「相位内第几帧」等分映射到「这一段有几张图」，单调不回头、不越界。</summary>
    private static int PhaseFrame(int frameInPhase, int phaseFrames, int spriteFrames) =>
        Math.Clamp(frameInPhase * spriteFrames / Math.Max(1, phaseFrames), 0, Math.Max(0, spriteFrames - 1));

    /// <summary>循环动作每张图停几个物理帧。占位值，归 `GP-6` 实测收敛。</summary>
    private static int LoopTicks(string action) => action switch
    {
        "idle" => 8,
        "walk" => 4,
        _ => 3,
    };

    /// <summary>缺图时的几何占位：只画一个框加朝向线，**不按动作分形状**。</summary>
    /// <remarks>
    /// 原来这里按动作画过圆圈（闪避）与斜线（命中相）、还给重击换成品红 —— 那些是"没有图"期间
    /// 的替身。跳跃／闪避／轻重击都有真图之后它们成了死代码，留着只会让人以为占位还在用。
    /// **机制本身保留**：将来新增一个还没画的动作，它照旧退回这个框并在启动日志里点名。
    /// </remarks>
    public override void _Draw()
    {
        if (!Sprite.Visible)
        {
            // 占位几何也得跟着纵深偏移，否则缺图的动作在纵深上走动时画面不动（`ENG-15`）。
            // 偏移读可视根那一份，不在这里再算一次 —— 算第二遍就多了一处会漂的地方。
            DrawSetTransform(Visual.Position);
            var color = Combat.Motor.IsInvulnerable ? Colors.Cyan : Colors.White;
            DrawRect(new Rect2(-9, -28, 18, 28), color, false, 2);
            DrawLine(new Vector2(0, -18), new Vector2(Combat.Motor.Facing * 20, -18), color, 2);
        }
    }
}
