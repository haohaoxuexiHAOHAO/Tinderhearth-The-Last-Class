using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// `GP-14` 训练房：第一个把 A1 战斗的所有部件组装成一个能玩的侧视场景。**不是探针，是给作者玩的。**
/// </summary>
/// <remarks>
/// 此前每个部件各有一个独立 dev 场景（`PlayerDev`／`HitFeedbackDev`／`DepthDev`／`CombatDebugDev`），
/// 各自只摆出验它那一条所需的最小布景。它们证明了「每个部件单独拿出来是对的」，但没有任何地方证明
/// **把它们摆在一起还成立** —— 三轴移动、按段判定框、纵深感知命中、排序与影子、四件套打击反馈、帧
/// 步进叠层，同时在一个场景里跑起来是什么手感。这就是本场景存在的理由。
///
/// **不动 <c>Main.tscn</c>。** 那条启动探针链是 `UI-1` 的验收执行体（`verify.py` 跑产物与
/// `check_camera`／`check_hud`／`check_input_map` 都读它），本场景是**另一个** `res://` 场景，
/// 各走各的。
///
/// **组装口径沿用 dev 场景，不新造：**
/// <list type="bullet">
/// <item>地面按纵深带画成 48px 厚的带（口径同 <see cref="DepthDev"/>）：碰撞面在带中线，视觉带上下
/// 各 24px，于是角色在带内前后走时脚不离开画出来的地面。</item>
/// <item>主角 <c>ManualPhysics=true</c>，推进由本场景循环调（口径同 <see cref="CombatDebugDev"/>）——
/// 帧步进要自己掌控节奏，交给引擎默认回调就没法在暂停时停住。</item>
/// <item>推进走 <see cref="CombatDebugOverlay.ShouldAdvance"/> 与 <see cref="Hitstop"/> 同一个 gate：
/// 暂停或顿帧时当帧不推进，连排序也一起冻住（<see cref="DepthSortedLayer"/> 的调用契约）。</item>
/// </list>
///
/// **叠层默认关**（`GP-14` 验收）：进场看到的是干净画面，按 <c>V</c> 才开框。这与 `CombatDebugDev`
/// 那个「进场就开框」的调试展示相反 —— 那里开框是因为它整个存在就是为了看框；这里是训练房，框是
/// 需要时才叫出来的工具。
/// </remarks>
public partial class TrainingRoom : Node2D
{
    /// <summary>碰撞地面的世界 Y。视觉带以它为中线上下各 24px（同 <see cref="DepthDev"/>）。</summary>
    private const int GroundY = 200;

    /// <summary>主角出生 X。木桩摆在它右前方一段距离，走过去就能打到。</summary>
    private const int PlayerX = 0;

    /// <summary>受击靶离主角的横向距离：够近，走两步就到；又留出接近的余地。</summary>
    private const int TargetOffsetX = 64;

    /// <summary>可跑动地面的半宽，世界像素。给足横向奔跑的余地。</summary>
    private const int GroundHalfWidth = 1500;

    /// <summary>叠层的 <c>z_index</c>：压在排序角色（<see cref="DepthSortedLayer.ActorZBase"/> 起）之上。</summary>
    private const int OverlayZ = 1000;

    /// <summary>打击特效的 <c>z_index</c>：压在角色之上、调试叠层之下（`GP-20`）。</summary>
    private const int SparkZ = 500;

    private InputRouter _router = null!;
    private DepthSortedLayer _layer = null!;
    private PlayerActor _player = null!;
    private PlayerActor _target = null!;
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
        var playerHurt = new Hurtbox { Actor = _player, HeightWorldPx = PlayerActor.BodyHeightWorldPx };
        _player.AddChild(playerHurt);
        _hitbox = new Hitbox();
        _player.AddChild(_hitbox);

        // 受击靶：一个被动的测试角色（`GP-14`）。不再用木桩几何 —— 靶是真角色，命中放受击帧作反馈
        // （替代木桩那种晃眼闪白）。ActorId 与主角区分开（各自一份 Controllers，本不冲突，区分只为清楚）。
        _target = new PlayerActor
        {
            ManualPhysics = true, ActorId = "target", Position = new Vector2(PlayerX + TargetOffsetX, GroundY),
        };
        _target.Controllers.Assign(_target.ActorId, new StationaryController());
        _layer.AddChild(_target);
        var targetHurt = new Hurtbox { Actor = _target, HeightWorldPx = PlayerActor.BodyHeightWorldPx };
        _target.AddChild(targetHurt);
        // 主角与靶互不实体碰撞:靶只跟地面碰。否则同层会互相挡,且是**不看纵深**地挡(比穿过去更糟,归
        // `GP-17`);而挨打时靶做 MoveAndSlide 又会把叠在一起的两者挤开 —— 正是作者实机撞到的「打一下靶
        // 弹到身前」。碰撞豁免后两者互不干涉,靶挪多少只由击退决定,不由脱离接触的挤出决定。
        _target.AddCollisionExceptionWith(_player);
        _player.AddCollisionExceptionWith(_target);

        // 叠层默认关（`GP-14`）：读的是命中查询与碰撞形状的同一份几何，不自己重算（`ENG-6`）。
        _overlay = new CombatDebugOverlay
        {
            Hitbox = _hitbox,
            Hurtboxes = [playerHurt, targetHurt],
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

    /// <summary>地面：碰撞面在带中线，视觉是一条 48px 厚的带（口径同 <see cref="DepthDev"/>）。</summary>
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
    /// 帧调优叠层的交互键。**不过 InputMap**，登记在 `check_input_map.py` 的 `HARNESS_KEYS`
    /// 与 `HARNESS_KEY_FILES`（键与 `CombatDebugDev` 相同，共用那三个已登记的键位）。
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
            _target.AdvanceCombat();
            _layer.Sort();
        }
    }

    /// <summary>命中触发的表现：顿帧（轻重都有）、震屏（仅重击）、命中点打击特效（`GP-20`，手绘 4 帧火花）。受击帧与击退在靶侧。</summary>
    private void OnHit(HitReaction reaction)
    {
        _stop.Begin(reaction.HitstopFrames);
        if (reaction.IsHeavy)
        {
            _camera.Rig.Shake();
        }

        // 命中点爆一个打击特效（`GP-20` 占位）：命中点取判定框此刻的世界中心，压在角色之上、播完自消。
        var spark = new HitSpark { Heavy = reaction.IsHeavy, ZIndex = SparkZ };
        AddChild(spark);
        spark.GlobalPosition = _hitbox.GlobalPosition;

        // 打击音 + 受击音（`GP-20` 占位借件）。**放在这里就是为了同帧**：本方法由 `Hitbox.Resolve`
        // 在命中当帧调，顿帧、震屏、火花、白闪（受击方 `Receive` 里）都落在这一帧，声音也必须。
        _audio.PlayHit();
    }
}
