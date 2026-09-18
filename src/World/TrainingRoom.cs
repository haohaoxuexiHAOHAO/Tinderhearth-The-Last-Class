using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// `GP-14` 训练房：第一个把 A1 战斗的所有部件组装成一个能玩的侧视场景。**不是探针，是给作者玩的。**
/// </summary>
/// <remarks>
/// 它存在的理由是：各部件单独看都对，但没有任何地方证明**把它们摆在一起还成立** —— 三轴移动、按段
/// 判定框、纵深感知命中、排序与影子、打击反馈、帧步进叠层，同时跑起来是什么手感。这只能实机看。
///
/// **本文件正在等一次重写，别照它现在的样子加东西。** 按 `ADR-0009` 的分工，场景该由作者在 Godot
/// 里搭（地面、主角、碰撞区域、沙包、障碍物、参数），脚本只负责读节点与接线；而现在这里是反的 ——
/// 整个场景在 <c>_Ready()</c> 里 <c>AddChild</c> 出来，编辑器里打开只看得到一个空 <c>Node2D</c>。
/// 改造归 `GP-14`（训练房先行）与 `ENG-21`（其余场景跟上）。
///
/// **组装口径**（这些数原本抄自已删除的各个 dev 场景，现在这里是唯一的一份）：
/// <list type="bullet">
/// <item>地面按纵深带画成 48px 厚的带：碰撞面在带中线，视觉带上下各 24px，于是角色在带内前后走时脚
/// 不离开画出来的地面。48px 够不够归 `ENG-16`。</item>
/// <item>主角 <c>ManualPhysics=true</c>，推进由本场景循环调 —— 帧步进要自己掌控节奏，交给引擎默认
/// 回调就没法在暂停时停住。</item>
/// <item>推进走 <see cref="CombatDebugOverlay.ShouldAdvance"/> 与 <see cref="Hitstop"/> 同一个 gate：
/// 暂停或顿帧时当帧不推进，连排序也一起冻住（<see cref="DepthSortedLayer"/> 的调用契约）。</item>
/// </list>
///
/// **叠层默认关**：进场看到的是干净画面，按 <c>V</c> 才开框 —— 这是训练房，框是需要时才叫出来的工具。
/// </remarks>
public partial class TrainingRoom : Node2D
{
    /// <summary>碰撞地面的世界 Y。视觉带以它为中线上下各 24px。</summary>
    private const int GroundY = 200;

    /// <summary>主角出生 X。沙包摆在它右前方一段距离，走过去就能打到。</summary>
    private const int PlayerX = 0;

    /// <summary>我方沙包离主角的横向距离：够近，走两步就到；又留出接近的余地。</summary>
    private const int AllyTargetOffsetX = 64;

    /// <summary>
    /// 敌方沙包再往右一段。**两个沙包分开站**，于是走一趟就能连着撞出两种结果（`GP-17`）：
    /// 我方那个穿得过去，敌方那个挡住、得往纵深挪半步才绕得过。
    /// </summary>
    private const int EnemyTargetOffsetX = 144;

    /// <summary>可跑动地面的半宽，世界像素。给足横向奔跑的余地。</summary>
    private const int GroundHalfWidth = 1500;

    /// <summary>叠层的 <c>z_index</c>：压在排序角色（<see cref="DepthSortedLayer.ActorZBase"/> 起）之上。</summary>
    private const int OverlayZ = 1000;

    /// <summary>打击特效的 <c>z_index</c>：压在角色之上、调试叠层之下（`GP-20`）。</summary>
    private const int SparkZ = 500;

    private InputRouter _router = null!;
    private DepthSortedLayer _layer = null!;
    private PlayerActor _player = null!;
    private PlayerActor _targetAlly = null!;
    private PlayerActor _targetEnemy = null!;
    private readonly DepthBlocker _blocker = new();
    private Hitbox _hitbox = null!;
    private CombatDebugOverlay _overlay = null!;
    private GameCamera _camera = null!;
    private CombatAudio _audio = null!;
    private readonly Hitstop _stop = new();

    /// <summary>本次挥击到此为止命中了几个目标。挥空音要靠它判「这一挥全空」（`GP-20`）。</summary>
    private int _swingHits;

    /// <summary>上一帧判定框开着没有。用来认出 Active 窗的下降沿，也就是「这一挥结束了」。</summary>
    private bool _wasHitActive;

    public override void _Ready()
    {
        _router = new InputRouter();
        AddChild(_router);

        BuildTerrain();

        _layer = new DepthSortedLayer();
        AddChild(_layer);

        _player = new PlayerActor { ManualPhysics = true, Position = new Vector2(PlayerX, GroundY) };
        _player.Controllers.Assign(_player.ActorId,
            new LocalPlayerController(_player.ActorId) { Router = _router });
        _layer.AddChild(_player);
        var playerHurt = AddHurtbox(_player);
        _hitbox = new Hitbox();
        _player.AddChild(_hitbox);

        // 两个受击沙包：一个我方、一个敌方（`GP-17` 的实机口径）。**两种阵营结果
        // 一趟就能试到** —— 走到我方那个身上穿得过去，走到敌方那个跟前被挡住。靶仍是真角色而不是木桩
        // 几何（`GP-14`）：命中放受击帧作反馈，替代木桩那种晃眼闪白。ActorId 各自区分（各有一份
        // Controllers，本不冲突，区分只为日志里认得出谁是谁）。
        _targetAlly = AddTarget("target-ally", AllyTargetOffsetX, CombatSide.Ally);
        _targetEnemy = AddTarget("target-enemy", EnemyTargetOffsetX, CombatSide.Enemy);
        var allyHurt = AddHurtbox(_targetAlly);
        var enemyHurt = AddHurtbox(_targetEnemy);

        // 实体阻挡（`GP-17`）：**豁免改由纵深与阵营逐帧决定，这里不再写死一条。**
        // 原先主角与靶是无条件互免碰撞，那行注释记着两个理由，现在两条都由判定本身兑现：
        // 「不看纵深地挡比穿过去更糟」→ 敌对那一对只在纵深同排时才挡，不同排不会互相卡住；
        // 「挨打时 MoveAndSlide 把叠在一起的两者挤开」（作者实机撞到的「打一下靶弹到身前」）→
        // 同阵营那一对永远豁免，而敌对那一对挡住之后两者本来就不会叠在一起，也就没有可挤的重叠。
        _blocker.Add(_player);
        _blocker.Add(_targetAlly);
        _blocker.Add(_targetEnemy);

        // 叠层默认关（`GP-14`）：读的是命中查询与碰撞形状的同一份几何，不自己重算（`ENG-6`）。
        _overlay = new CombatDebugOverlay
        {
            Hitbox = _hitbox,
            Hurtboxes = [playerHurt, allyHurt, enemyHurt],
            ZIndex = OverlayZ,
        };
        AddChild(_overlay);

        // 相机自行推进（不设 ManualAdvance）：跟随与震动衰减各帧照走，与战斗推进的 gate 解耦，
        // 于是顿帧冻结战斗时相机仍在把这一下的屏幕震动放出来 —— 冻结 + 震动同时发生正是「打得实」。
        _camera = new GameCamera(CameraView.SideView) { FollowTarget = _player, Router = _router };
        AddChild(_camera);

        // 声音（`GP-20` 占位借件）：命中当帧与顿帧/白闪/火花同一帧响，挥空另有一声。
        _audio = new CombatAudio();
        AddChild(_audio);
    }

    /// <summary>造一个被动的受击沙包并接进排序层。立场由调用方给（`GP-17`）。</summary>
    private PlayerActor AddTarget(string actorId, int offsetX, CombatSide side)
    {
        var target = new PlayerActor
        {
            ManualPhysics = true,
            ActorId = actorId,
            Side = side,
            Position = new Vector2(PlayerX + offsetX, GroundY),
        };
        target.Controllers.Assign(actorId, new StationaryController());
        _layer.AddChild(target);
        return target;
    }

    /// <summary>受击框按角色实测本体建（`GP-19`：高度必填、不许沿用木桩那个 32）。</summary>
    private static Hurtbox AddHurtbox(PlayerActor actor)
    {
        var hurtbox = new Hurtbox { Actor = actor, HeightWorldPx = PlayerActor.BodyHeightWorldPx };
        actor.AddChild(hurtbox);
        return hurtbox;
    }

    /// <summary>地面：碰撞面在带中线，视觉是一条 48px 厚的带。</summary>
    private void BuildTerrain()
    {
        var ground = new StaticBody2D { Position = new Vector2(PlayerX, GroundY) };
        ground.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(GroundHalfWidth * 2, 400) },
            Position = new Vector2(0, 200),
        });
        AddChild(ground);

        var half = DepthBand.WidthWorldPx / 2;
        AddChild(new Polygon2D
        {
            Polygon =
            [
                new(-GroundHalfWidth, GroundY - half), new(GroundHalfWidth, GroundY - half),
                new(GroundHalfWidth, GroundY + half), new(-GroundHalfWidth, GroundY + half),
            ],
            Position = new Vector2(PlayerX, 0),
            Color = new Color("485750"),
            ZIndex = -1,
        });
    }

    /// <summary>
    /// 帧调优叠层的交互键。**刻意不过 InputMap** —— 它们是调试工具的键，不是玩法绑定，混进 InputMap
    /// 会让「玩家能重绑的键」这份清单里多出三个玩家根本不该看见的条目。原先登记在
    /// `check_input_map.py` 里，那个守卫已随 `ADR-0009` 删除，现在这里是唯一的一份。
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.V: _overlay.Enabled = !_overlay.Enabled; break;
            case Key.P: _overlay.Paused = !_overlay.Paused; break;
            case Key.Period: _overlay.RequestStep(); break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // 暂停（帧步进）或顿帧时当帧不推进；连排序一起冻住，绘制前后关系不在冻结帧里跳动。
        if (_overlay.ShouldAdvance() && !_stop.Tick())
        {
            // **阻挡排在所有推进之前**（`GP-17`）：豁免要在这一帧的 MoveAndSlide 之前就位，否则挡开
            // 会晚一帧 —— 表现是贴着敌人时能插进去一格再被弹回来。代价是它用的是上一帧的纵深，60Hz
            // 下纵深每帧最多走 1px，相对 8px 的阈值是 1px 误差，理由见 <see cref="DepthBlocker"/>。
            // 冻结帧不调：那几帧没人移动，豁免表也就不必变。
            _blocker.Resolve();
            _player.AdvanceCombat();
            // **命中结算排在靶推进之前**（`GP-14` 阶段 1 实机修）：命中让靶进入受击相位，靶必须在
            // **这一帧**就把受击帧显出来，随后的顿帧才冻在受击姿上。反过来（先推靶再结算）会让靶这一
            // 帧还是待机姿，顿帧冻在待机上、受击帧要等冻结结束才开始 —— 命中那一下看着像没反应，正是
            // 作者实机说的「打击感弱、受击一闪而过」。靶是被动的，位置不随这个顺序变，所以命中检测不受影响。
            _swingHits += _hitbox.Resolve(_player, OnHit);
            // 挥空音（`GP-20`）：认判定框的**下降沿** —— Active 窗刚走完而这一挥零命中，就是打空了。
            // 判在场景层而不是规则层：规则层不认识声音，而「这一挥有没有碰到东西」正是命中检测的
            // 返回值，本来就在手上。收招被落地打断时同样算空，那也确实没打着。
            var hitActive = _player.Combat.Combo.IsHitActive;
            if (_wasHitActive && !hitActive)
            {
                if (_swingHits == 0)
                {
                    _audio.PlayMiss();
                }
                _swingHits = 0;
            }
            _wasHitActive = hitActive;
            _targetAlly.AdvanceCombat();
            _targetEnemy.AdvanceCombat();
            _layer.Sort();
        }
    }

    /// <summary>命中触发的表现：顿帧（轻重都有）、震屏（仅重击）、命中点打击特效（`GP-20`，手绘 4 帧火花）。受击帧与击退在靶侧。</summary>
    private void OnHit(HitReaction reaction)
    {
        _stop.Begin(reaction.HitstopFrames);
        // 震屏轻重各一组（`GP-20`）：**轻击也震，只是微震** —— 原先轻击命中镜头毫无反应，正是作者
        // 说的「轻击就一下」。顿帧长度不动（轻 3／重 5 帧）：拉长顿帧只会更像延迟，爆点该由同帧的
        // 火花、白闪、声音与这一抖去承担。相机不受顿帧 gate 冻结，所以这一抖在冻结帧里就放出来了。
        var (shakeAmplitude, shakeSeconds) = CameraFeel.HitShake(reaction.IsHeavy);
        _camera.Rig.Shake(shakeAmplitude, shakeSeconds);

        // 命中点爆一个打击特效（`GP-20` 占位）：命中点取判定框此刻的世界中心，压在角色之上、播完自消。
        var spark = new HitSpark { Heavy = reaction.IsHeavy, ZIndex = SparkZ };
        AddChild(spark);
        spark.GlobalPosition = _hitbox.GlobalPosition;

        // 打击音 + 受击音（`GP-20` 占位借件）。**放在这里就是为了同帧**：本方法由 `Hitbox.Resolve`
        // 在命中当帧调，顿帧、震屏、火花、白闪（受击方 `Receive` 里）都落在这一帧，声音也必须。
        _audio.PlayHit();
    }
}
