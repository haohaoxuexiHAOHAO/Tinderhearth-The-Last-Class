using Godot;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.World;

/// <summary>主角节点。横向与跳跃的位置归物理引擎，动作、速度与纵深归规则层。</summary>
/// <remarks>训练房里那两个受击沙包也是这个类，只是换成一个不操作的控制器。</remarks>
public partial class PlayerActor : CharacterBody2D, IBlockingActor, IHittable
{
    // ── 精灵表几何 ───────────────────────────────────────────
    // 下面三个数是从素材原件量出来的帧框：帧宽、帧高、帧内的地面行。载入时会按纹理实际尺寸核一遍
    // （高必须等于帧高、宽必须是帧宽的整数倍），对不上就当缺图走占位分支并打日志。于是「精灵表
    // 重新生成后帧框变了而代码没跟着改」不会静默错位。
    //
    // 但这个自校验管得住宽高，管不住地面行。GroundRow 只影响精灵往上抬多少，改错一像素的表现是
    // 脚底离地或陷进地面，而纹理尺寸照样对得上、日志照样干净。所以改完这三个数，请在 Godot 里跑
    // 一次训练房看一眼脚底有没有贴地 —— 那是一眼就看得出来的事。
    //
    // 这三个只有本类读，所以是 private。下面还有几个常量写成 internal，那是因为同一程序集里
    // 真有别处读它们，各自注明了是谁 —— 没有读者就不要放宽，放宽之后「谁在用这个数」就查不清了。
    private const int FrameWidth = 44;
    private const int FrameHeight = 32;

    /// <summary>帧内的地面行，单位是帧内像素行号。脚底像素落在它上面一行。</summary>
    /// <remarks>精灵的偏移量就是把这一行对到节点原点上算出来的，而节点原点就是脚底。</remarks>
    private const int GroundRow = 30;

    /// <summary>角色身体的宽度，世界像素。碰撞框用它。</summary>
    /// <remarks>
    /// 只有本类读它。实体阻挡要的半宽是从引擎实际在用的那个碰撞形状上读回来的，不是读这个常量
    /// （见 <see cref="DepthBlocker"/> 的 HalfWidthOf），所以这里不必放宽成 internal。
    ///
    /// 贴着实测轮廓来：站立姿量出来约 19 像素宽，取 18 当身体宽。
    /// </remarks>
    private const int BodyWidthWorldPx = 18;

    /// <summary>主角站立时的身体高度，世界像素。实体碰撞框与受击框共用它，不各写一个数。</summary>
    /// <remarks>
    /// 它是 internal，因为 <see cref="TrainingRoom"/> 挂受击框时要读它，那边不该再抄一遍数字。
    ///
    /// 28 是站立姿实测量出来的（去掉地面参考线之后量的）。受击框原先写死 32，那是木桩柱子的高度，
    /// 套到主角身上高出 4 像素，从头顶掠过的攻击照样判命中，而且不报错。
    ///
    /// 各动作里最高的姿态能到 30 像素（闪避与重击），这里刻意不取那个数。宁可闪步那几帧头顶露出
    /// 框外一点（那几帧本来就是在主动闪避），也不要为了兜住最高姿态让站着的时候平白高 2 像素。
    /// 按状态给不同高度才是长久的做法，那要先定设计。
    /// </remarks>
    internal const int BodyHeightWorldPx = 28;

    private const string SheetDir = "res://assets/self-drawn/test-role";

    /// <summary>脚下用哪一套影子图（见 <see cref="DepthVisual.LoadShadowSteps"/>）。</summary>
    /// <remarks>
    /// 主角是人形，走体型表里「人」那一档；体型表在设计仓 canon/gameplay/玩法定位.md 的「像素基准」一节。
    /// 帧框改成按角色各自声明时（设计仓台账 ENG-24），这一项跟着进那份声明。
    /// </remarks>
    private const string ShadowSet = "human";

    // 已经接进玩法的那些精灵表。轻击三段各有一张（light、light2、light3），由 UpdateVisual 按当前
    // 打到第几段来选；受击分轻重两张，命中时放受击帧作反馈。
    //
    // 防御、精准防御、失衡与死亡那几张刻意不载入：对应的玩法规则还没有，载进来只会是「有图没规则」
    // 的半成品。失衡那张还多一层不确定 —— 失衡与失衡恢复画在同一张表里，作者还没说从第几帧切开。
    private static readonly string[] Sheets =
        ["idle", "walk", "run", "jump", "dodge", "light", "light2", "light3", "heavy", "light_hit", "heavy_hit"];

    // 攻击的三个阶段各用哪几张图。每张表的帧数都不同，所以映射按当前表实际有几帧算，不写死帧号。
    // 只有一条硬约束：前摇只准用第 0 帧，判定窗只准用第 1 到第 2 帧。
    //
    // 理由是作者每段的第 0 帧是静止起手姿、第 1 到 2 帧才是拳脚伸出去的命中姿，而判定框的尺寸正是
    // 从这两帧量出来的伸展换算的。让命中姿落在判定窗那两帧上，画面与判定才对得齐；要是让渲染时钟
    // 自己跑，命中姿会在判定框开之前先亮出来，玩家学到的时机就是错的。三段都逐段核过：轻击前两段
    // 的最大右伸在第 1 帧，第三段那一脚在第 2 帧，都落在这个区间里。
    // 三个都只有本类读。CombatFeel 那边量判定框尺寸时提到了这两个帧号，但它在规则层、是另一个
    // 程序集，本来就读不到 internal —— 那是一处写在注释里的口头约定，不是代码依赖。
    private const int AttackStartupFrame = 0;
    private const int AttackActiveFirstFrame = 1;
    private const int AttackActiveSpan = 2;

    public string ActorId { get; init; } = "player";

    /// <inheritdoc />
    /// <remarks>
    /// 默认我方，敌方由场景显式给。它与「谁在驱动这个角色」是两件事，不许互相推断 —— 一个由 AI
    /// 驱动的队友仍然是我方。
    /// </remarks>
    public CombatSide Side { get; init; } = CombatSide.Ally;

    public ActorControllerRegistry Controllers { get; init; } = new();
    public ActorCombatState Combat { get; } = new();
    public AnimatedSprite2D Sprite { get; } = new();
    public string VisualAction { get; private set; } = "idle";
    private int _visualFrame;

    /// <summary>最近挨的那一下是不是重击。决定受击时放哪一张受击表。</summary>
    private bool _hurtHeavy;

    /// <summary>着色器里那个白闪开关的参数名。只写一次，读写两处都引它。</summary>
    private static readonly StringName FlashParam = "flash";

    /// <summary>命中白闪用的着色器。写在代码里，不单独做成一个 <c>.gdshader</c> 文件。</summary>
    /// <remarks>
    /// 白闪必须走着色器：<c>Modulate</c> 是乘法，白色乘上彩色贴图等于原样，精灵根本不会变白
    /// （实测踩过）。木桩能靠换填充色变白，是因为它本来就是几何、没有贴图。
    ///
    /// 闪白由代码持有，不要让美术在受击表里塞一张纯白帧 —— 那在第三方素材里是常见做法（实测过一份
    /// 下载素材的受击表第一帧就是整张白的），但那样闪白时长就烧进图里了，代码调不了，也没法跟顿帧
    /// 一起冻结、做成可关的无障碍项。裁图时请把这种帧挑出去。
    ///
    /// 着色器只改 RGB、不动 alpha：本项目的像素只允许全透明或全不透明，动 alpha 会造出半透明边。
    /// 不闪的时候一个字都不写 <c>COLOR</c>，于是默认采样和 <c>Modulate</c> 那边的染色照旧生效，
    /// 两条路不抢。
    /// </remarks>
    private static readonly Shader FlashShader = new()
    {
        Code = """
            shader_type canvas_item;

            uniform bool flash = false;

            void fragment()
            {
                if (flash)
                {
                    COLOR.rgb = vec3(1.0);
                }
            }
            """,
    };

    /// <summary>本角色自己的一份着色器材质：着色器共享，那个开关各自一份。</summary>
    private readonly ShaderMaterial _flashMaterial = new() { Shader = FlashShader };

    /// <summary>白闪结束的物理帧号，不含这一帧。0 表示从没闪过。</summary>
    private ulong _flashUntilFrame;

    /// <summary>此刻在不在白闪。按物理帧号算，不靠每帧减一的计数器。</summary>
    /// <remarks>
    /// 用帧号是为了躲开一个顺序依赖：命中当帧里 <see cref="Receive"/>（由场景的命中结算调）和本
    /// 节点自己的 <see cref="_PhysicsProcess"/> 都会跑，而谁先谁后取决于节点树的顺序。用计数器的
    /// 话，白闪到底亮 1 帧还是 2 帧就成了节点树结构的函数，换一次挂载点就悄悄变。帧号跟调用顺序
    /// 和调用次数都无关。
    ///
    /// 顿帧不影响它：物理帧号照常走，被停住的只是战斗推进。白闪按真实帧走的理由见
    /// <see cref="CombatFeel.HitFlashFrames"/>。
    /// </remarks>
    public bool HitFlashing => Engine.GetPhysicsFrames() < _flashUntilFrame;

    /// <summary>白闪还剩几个物理帧。</summary>
    public int HitFlashFramesLeft =>
        (int)Math.Max(0L, (long)_flashUntilFrame - (long)Engine.GetPhysicsFrames());

    /// <summary>材质里那个开关此刻的实际值，从材质读回来的。</summary>
    /// <remarks>
    /// <see cref="HitFlashing"/> 说的是「按帧号算此刻该不该闪」，这一个说的是「开关真的送到材质
    /// 了吗」。只看前者的话，漏掉那一句 <c>SetShaderParameter</c> 也看不出来。
    /// </remarks>
    public bool HitFlashUniform => _flashMaterial.GetShaderParameter(FlashParam).AsBool();

    /// <summary>纵深可视根：精灵挂在它下面，纵深偏移与影子都由它管。</summary>
    public DepthVisual Visual { get; private set; } = null!;

    /// <inheritdoc />
    /// <remarks>只是转发规则层那一份，本类不另存一份纵深。</remarks>
    public double DepthWorldPx => Combat.Motor.DepthWorldPx;

    /// <inheritdoc />
    /// <remarks>
    /// 同样只是转发，写入口仍然唯一。引擎层只读纵深值，绝不自己再按速度积分一遍 —— 那会得到两倍
    /// 位移，而且不报错，见 <c>ARCHITECTURE.md</c>。
    ///
    /// 底下那个方法会把纵深速度清零，而这里不需要为此另开一个保速度的写法：纵深速度每帧都由输入
    /// 重算，而本方法由 <see cref="DepthBlocker"/> 在角色推进之前调，清掉的值在同一帧里就被重新算
    /// 出来了。于是「两个轴只要有一个在动就算在走」照旧成立，顶着障碍物挪纵深时角色不会显示待机。
    /// </remarks>
    public void PlaceDepth(double depthWorldPx) => Combat.Motor.PlaceDepth(depthWorldPx);

    /// <inheritdoc />
    public DepthSubject DepthSubject => Visual.Subject;

    /// <summary>哪些动作因为缺图退回了几何占位。空的就表示每个动作都有真图。</summary>
    public IReadOnlyList<string> MissingSheets => _missing;
    private readonly List<string> _missing = [];

    public override void _Ready()
    {
        if (Engine.PhysicsTicksPerSecond != CombatFeel.PhysicsTicksPerSecond)
        {
            throw new InvalidOperationException("战斗的帧数都按每秒 60 个物理帧算，项目里设的不是 60");
        }
        _ = Controllers.Require(ActorId);
        // 碰撞框贴实测轮廓，底边落在节点原点上 —— 原点就是脚底，精灵的偏移也对到同一处。
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(BodyWidthWorldPx, BodyHeightWorldPx) },
            Position = new Vector2(0, -14),
        });
        Sprite.SpriteFrames = new SpriteFrames();
        // 把帧内地面行对到节点原点上：帧是居中绘制的，所以第 r 行的上沿在精灵局部 y = r 减帧高一半。
        Sprite.Position = new Vector2(0, -(GroundRow - FrameHeight / 2));
        // 白闪走材质，不走 Modulate（乘法对彩色贴图无效），理由见 FlashShader。
        Sprite.Material = _flashMaterial;
        foreach (var name in Sheets)
        {
            var texture = GD.Load<Texture2D>($"{SheetDir}/{name}.png");
            if (texture == null || texture.GetHeight() != FrameHeight
                || texture.GetWidth() % FrameWidth != 0 || texture.GetWidth() == 0)
            {
                GD.PushWarning($"[主角] 精灵表缺失或尺寸不对，这个动作退回几何占位：{name}");
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
            // 把每张表的帧数打进启动日志，方便对照精灵表确认切图没切错。
            GD.Print($"[主角] 精灵表 {name} 有 {count} 帧");
        }
        GD.Print(_missing.Count == 0
            ? $"[主角] 全部动作都有真图；帧框 {FrameWidth}x{FrameHeight}，帧内地面行 {GroundRow}"
            : $"[主角] 这些动作缺图、退回几何占位：{string.Join(", ", _missing)}");
        // 精灵挂在纵深可视根下面，于是纵深偏移只有一处来源。会跳、会被打飞的角色，影子单画一张由
        // 可视根摆在地上，动作帧里不画。
        Visual = new DepthVisual { Actor = this, ShadowSteps = DepthVisual.LoadShadowSteps(ShadowSet) };
        AddChild(Visual);
        Visual.AddChild(Sprite);
        UpdateVisual();
    }

    /// <summary>置真表示由场景自己调 <see cref="AdvanceCombat"/>，引擎的默认物理回调就不再推进一遍。</summary>
    public bool ManualPhysics { get; init; }

    public override void _PhysicsProcess(double delta)
    {
        // 白闪开关每帧同步一次。这一句刻意放在 ManualPhysics 判断之外：白闪按真实帧走，顿帧或单帧
        // 前进冻住战斗推进的时候它仍要自己走完。
        _flashMaterial.SetShaderParameter(FlashParam, HitFlashing);
        if (!ManualPhysics) AdvanceCombat();
    }

    /// <summary>推进一个没被冻结的物理帧：读输入、移动、落地取消，再刷动画。</summary>
    public void AdvanceCombat()
    {
        var controller = Controllers.Require(ActorId);
        var input = controller is ICombatController combatController
            ? combatController.ReadCombatInput(new ActorView(ActorId)) : CombatInput.None;
        Combat.Tick(input, IsOnFloor());
        Velocity = new Vector2((float)Combat.Motor.HorizontalVelocity, (float)Combat.Motor.VerticalVelocity);
        MoveAndSlide();
        Combat.AfterMove(IsOnFloor(), IsOnCeiling(), IsOnWall(), Velocity.X);
        // 位置定下来之后才刷纵深偏移与影子：找地面那条射线要问的是这一帧的最终位置。
        Visual.Sync();
        UpdateVisual();
    }

    /// <summary>挨一下。实现 <see cref="IHittable"/>。</summary>
    /// <remarks>
    /// 命中反应分在三处：顿帧与震屏在场景那一层（命中回调里做，木桩与角色通用）；硬直与击退在
    /// 这里，交给运动状态机的 <see cref="MotorState.Stagger"/>；受击帧在
    /// <see cref="UpdateVisual"/> 里按运动相位挑。轻击的击退量是 0，所以轻击命中是「定身加受击帧」
    /// 没有位移，重击才推开。
    /// </remarks>
    public void Receive(HitReaction reaction, int facing)
    {
        _hurtHeavy = reaction.IsHeavy;
        // 白闪在命中当帧就打戳。这一句跑在顿帧开始之前（命中结算里先通知目标、后做反馈），所以
        // 白闪、受击帧、顿帧、震屏与火花全部落在同一个物理帧上，那是「爆点」而不是「延迟」的条件。
        _flashUntilFrame = Engine.GetPhysicsFrames() + (ulong)CombatFeel.HitFlashFrames;
        _flashMaterial.SetShaderParameter(FlashParam, true);
        // 把「在硬直帧数内走完这段击退距离」折成速度：每帧位移等于击退除硬直，速度等于它乘帧率。
        var knockbackVelocity = reaction.HitstunFrames > 0
            ? facing * (double)reaction.KnockbackWorldPx / reaction.HitstunFrames * CombatFeel.PhysicsTicksPerSecond
            : 0.0;
        Combat.Motor.Stagger(reaction.HitstunFrames, knockbackVelocity);
    }

    private void UpdateVisual()
    {
        var motor = Combat.Motor;
        var combo = Combat.Combo;
        var action = motor.Phase switch
        {
            MotorPhase.Hurt => _hurtHeavy ? "heavy_hit" : "light_hit",
            _ when combo.IsAttacking => combo.Kind == ComboKind.Heavy ? "heavy" : LightSheet(combo.Step),
            MotorPhase.Dodge => "dodge",
            MotorPhase.Airborne => "jump",
            // 地面只有行走与奔跑两档，这张表只在奔跑那一档播。
            MotorPhase.Run => "run",
            // 横向与纵深，两个轴只要有一个在动就算「在走」。原来只看横向速度，于是纯前后移动时放的
            // 是待机 —— 角色站着不动地在纵深上滑，而它不报错，只有眼睛看得出来。走纵深直接复用侧面
            // 行走姿态，不需要新素材：这一类横版作品都没有「往里走」的专用动画。
            _ when Math.Abs(motor.HorizontalVelocity) > 0 || Math.Abs(motor.DepthVelocity) > 0 => "walk",
            _ => "idle",
        };
        _visualFrame = action == VisualAction ? _visualFrame + 1 : 0;
        VisualAction = action;
        Sprite.Visible = Sprite.SpriteFrames.HasAnimation(action);
        Sprite.FlipH = motor.Facing < 0;
        Sprite.Modulate = motor switch
        {
            { IsInvulnerable: true } => Colors.Cyan,
            { Phase: MotorPhase.Run } => Colors.Yellow,
            _ => Colors.White,
        };
        if (Sprite.Visible)
        {
            Sprite.Animation = action;
            var count = Sprite.SpriteFrames.GetFrameCount(action);
            // 攻击与闪避按规则层算出来的相位取帧，不让渲染时钟自己跑；其余动作才按渲染计数循环。
            Sprite.Frame = motor.Phase switch
            {
                // 受击帧播一遍就停在末帧，不循环。
                MotorPhase.Hurt => Math.Min(count - 1, _visualFrame / LoopTicks(action)),
                _ when combo.IsAttacking => AttackFrame(combo, count),
                MotorPhase.Dodge => PhaseFrame(_visualFrame, CombatFeel.DodgeDurationFrames, count),
                _ when action == "jump" => Math.Min(count - 1, _visualFrame / LoopTicks(action)),
                _ => (_visualFrame / LoopTicks(action)) % count,
            };
        }
        QueueRedraw();
    }

    /// <summary>轻击每一段用哪张精灵表。</summary>
    /// <remarks>
    /// 段号超出范围（照理不会）落到最后一段，与 <see cref="Hitbox.SpecFor"/> 的兜底方向一致 ——
    /// 两处对「段号不认识」的处理必须同向，否则动画与判定框会各选一段。
    /// </remarks>
    private static string LightSheet(int step) => step switch
    {
        0 => "light",
        1 => "light2",
        _ => "light3",
    };

    /// <summary>一段攻击此刻该放第几帧图。命中姿绝不出现在前摇里，理由见类顶部那段注释。</summary>
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

    /// <summary>把「这个阶段走到第几帧」等分摊到「这一段有几张图」，不回头也不越界。</summary>
    private static int PhaseFrame(int frameInPhase, int phaseFrames, int spriteFrames) =>
        Math.Clamp(frameInPhase * spriteFrames / Math.Max(1, phaseFrames), 0, Math.Max(0, spriteFrames - 1));

    /// <summary>循环播放的动作里，每张图停几个物理帧。</summary>
    /// <remarks>这几个数是临时填的，手感要实机试了才定得下来。</remarks>
    private static int LoopTicks(string action) => action switch
    {
        "idle" => 8,
        "walk" => 4,
        _ => 3,
    };

    /// <summary>缺图时的几何占位：只画一个框加一条朝向线，不按动作分形状。</summary>
    /// <remarks>
    /// 这套占位机制保留着：将来新增一个还没画的动作，它照旧退回这个框，并在启动日志里点名是哪个。
    /// </remarks>
    public override void _Draw()
    {
        if (!Sprite.Visible)
        {
            // 占位几何也得跟着纵深偏移，否则缺图的动作在纵深上走动时画面不动。偏移读可视根那一份，
            // 不在这里再算一次 —— 算第二遍就多了一处会跟不上的地方。
            DrawSetTransform(Visual.Position);
            var color = Combat.Motor.IsInvulnerable ? Colors.Cyan : Colors.White;
            DrawRect(new Rect2(-9, -28, 18, 28), color, false, 2);
            DrawLine(new Vector2(0, -18), new Vector2(Combat.Motor.Facing * 20, -18), color, 2);
        }
    }
}
