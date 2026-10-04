using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// 训练房：把战斗的各个部件组装成一个能上手玩的侧视场景，用来实机看手感。
/// </summary>
/// <remarks>
/// 它存在的理由是各部件单独看都对，但「摆在一起还成立吗」只能实机看 —— 三个轴的移动、按段换的
/// 判定框、带纵深的命中、排序与影子、打击反馈、单帧前进的叠层，同时跑起来是什么感觉。
///
/// 本文件正在等一次重写，别照它现在的样子往里加东西。场景本该由作者在 Godot 里搭（地面、主角、
/// 碰撞区域、沙包、障碍物、参数都填在节点上），脚本只负责读节点、把它们接到规则上；而现在这里是
/// 反的 —— 整个场景在 <c>_Ready</c> 里建出来，编辑器里打开只看得到一个空的 <see cref="Node2D"/>。
///
/// 组装的几条口径：
/// <list type="bullet">
/// <item>地面画成一条跟可行走纵深一样厚的带，碰撞面在带的中线上，于是角色在带内前后走时脚不会
/// 离开画出来的地面。</item>
/// <item>几个角色都关掉引擎默认的物理回调，由本场景的循环显式推进 —— 单帧前进要自己掌控节奏，
/// 交给引擎回调就没法在暂停时停住。</item>
/// <item>推进只看一个闸门：暂停或顿帧时这一帧不推进，连排序也一起冻住，于是绘制的前后关系不会在
/// 冻结帧里跳动。</item>
/// </list>
/// </remarks>
public partial class TrainingRoom : Node2D
{
    /// <summary>碰撞地面在世界坐标的哪个 Y 上。画出来的那条带以它为中线。</summary>
    private const int GroundY = 200;

    /// <summary>主角出生在哪个 X 上。沙包摆在它右边一段，走过去就能打到。</summary>
    private const int PlayerX = 0;

    /// <summary>我方沙包离主角多远，世界像素。够近，走两步就到，又留出一点接近的余地。</summary>
    private const int AllyTargetOffsetX = 64;

    /// <summary>敌方沙包再往右一段，世界像素。</summary>
    /// <remarks>
    /// 两个沙包分开站，于是走一趟就能连着撞出两种结果：我方那个穿得过去，敌方那个挡住、
    /// 得往前后挪半步才绕得过。
    /// </remarks>
    private const int EnemyTargetOffsetX = 144;

    /// <summary>可跑动地面的半宽，世界像素。给足横向奔跑的余地。</summary>
    private const int GroundHalfWidth = 1500;

    /// <summary>调试叠层的 <c>z_index</c>：压在所有参与排序的角色之上。</summary>
    private const int OverlayZ = 1000;

    /// <summary>打击火花的 <c>z_index</c>：压在角色之上、调试叠层之下。</summary>
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

    /// <summary>这一挥到此为止打中了几个。挥空音靠它判断「这一挥全空」。</summary>
    private int _swingHits;

    /// <summary>上一帧判定窗开着没有。靠它认出窗口刚关的那一帧，也就是「这一挥结束了」。</summary>
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

        // 两个受击沙包，一个我方一个敌方，于是走一趟就能把两种阵营结果都试到。靶用真角色而不是木桩
        // 几何，是为了让命中能放受击帧作反馈。两个各起一个 ActorId，只为日志里认得出谁是谁。
        _targetAlly = AddTarget("target-ally", AllyTargetOffsetX, CombatSide.Ally);
        _targetEnemy = AddTarget("target-enemy", EnemyTargetOffsetX, CombatSide.Enemy);
        var allyHurt = AddHurtbox(_targetAlly);
        var enemyHurt = AddHurtbox(_targetEnemy);

        // 谁挡谁由纵深与阵营逐帧判，这里不写死任何一对的豁免。原先主角与靶是无条件互不碰撞的，
        // 那样做要躲的两件事现在由判定本身兑现：敌对那一对只在纵深同排时才挡，不同排不会互相卡住；
        // 同阵营那一对永远豁免，所以不会再出现实机撞到过的「打一下靶就弹到身前」。
        _blocker.Add(_player);
        _blocker.Add(_targetAlly);
        _blocker.Add(_targetEnemy);

        // 叠层默认关着：进场看到的是干净画面，按 V 才把框叫出来。
        _overlay = new CombatDebugOverlay
        {
            Hitbox = _hitbox,
            Hurtboxes = [playerHurt, allyHurt, enemyHurt],
            ZIndex = OverlayZ,
        };
        AddChild(_overlay);

        // 相机自己推进，不受战斗那个闸门管：跟随与震动衰减每帧照走，于是顿帧冻住战斗的时候相机仍在
        // 把这一下的震动放出来。战斗冻住、画面在抖，这两件事同时发生才是「打得实」。
        _camera = new GameCamera(CameraView.SideView) { FollowTarget = _player, Router = _router };
        AddChild(_camera);

        // 声音：命中当帧和顿帧、白闪、火花同一帧响；挥空另有一声。
        _audio = new CombatAudio();
        AddChild(_audio);
    }

    /// <summary>造一个不还手的受击沙包并挂进排序层。它站哪一边由调用方给。</summary>
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

    /// <summary>给一个角色挂上受击框，高度取这个角色实测的本体高度。</summary>
    private static Hurtbox AddHurtbox(PlayerActor actor)
    {
        var hurtbox = new Hurtbox { Actor = actor, HeightWorldPx = PlayerActor.BodyHeightWorldPx };
        actor.AddChild(hurtbox);
        return hurtbox;
    }

    /// <summary>搭地面：碰撞面落在带的中线上，看得见的是一条跟可行走纵深一样厚的带。</summary>
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

    /// <summary>调试叠层的几个按键：V 开关框，P 暂停，句点键往前走一帧。</summary>
    /// <remarks>
    /// 刻意不走 InputMap。它们是调试工具的键、不是玩法绑定，登进 InputMap 会让「玩家能重绑的键」
    /// 那份清单里多出几个玩家根本不该看见的条目。所以这几个键只在这里出现一次。
    /// </remarks>
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
        // 暂停或顿帧时这一帧什么都不推进，排序也一起冻住，绘制的前后关系不会在冻结帧里跳动。
        if (_overlay.ShouldAdvance() && !_stop.Tick())
        {
            // 阻挡排在所有推进之前：豁免要在这一帧的 MoveAndSlide 之前就位，否则挡开会晚一帧，
            // 表现是贴着敌人时能先插进去一点再被弹回来。代价是它用的是上一帧的纵深，误差 1 像素，
            // 理由见 <see cref="DepthBlocker"/>。冻结帧不调：那几帧没人移动，豁免表也就不必变。
            _blocker.Resolve();
            _player.AdvanceCombat();
            // 命中结算排在靶推进之前：命中让靶进入受击状态，靶必须在这一帧就把受击帧显出来，随后的
            // 顿帧才冻在受击姿上。顺序反过来的话，靶这一帧还是待机姿，顿帧冻在待机上、受击帧要等
            // 冻结结束才开始 —— 那一下看着像没反应，正是实机反馈的「打击感弱、受击一闪而过」。
            // 靶是被动的，位置不随这个顺序变，所以命中检测本身不受影响。
            _swingHits += _hitbox.Resolve(_player, OnHit);
            // 挥空音认的是判定窗刚关的那一帧：窗走完了而这一挥零命中，就是打空了。判在场景这一层
            // 而不是规则层，因为规则层不认识声音，而「这一挥有没有碰到东西」正是命中检测的返回值，
            // 本来就在手上。收招被落地打断时同样算空，那也确实没打着。
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

    /// <summary>打中一下之后，攻击方这一侧的表现：顿帧、震屏、命中点的火花、声音。</summary>
    /// <remarks>
    /// 受击帧与击退在挨打那一方自己做。这几样全写在这一个方法里就是为了同帧 —— 它由
    /// <see cref="Hitbox.Resolve"/> 在命中当帧调，顿帧、震屏、火花、白闪与声音因此都落在同一帧上，
    /// 那是「爆点」而不是「延迟」的条件。
    /// </remarks>
    private void OnHit(HitReaction reaction)
    {
        _stop.Begin(reaction.HitstopFrames);
        // 轻重各一组震屏幅度，轻击也震、只是微震 —— 原先轻击命中镜头毫无反应，实机读起来就是「没
        // 打到什么」。顿帧长度刻意不跟着拉长：拉长只会更像延迟，那一下的力量该由同一帧的火花、
        // 白闪、声音和这一抖去撑。相机不受战斗那个闸门冻结，所以这一抖在冻结帧里就放出来了。
        var (shakeAmplitude, shakeSeconds) = CameraFeel.HitShake(reaction.IsHeavy);
        _camera.Rig.Shake(shakeAmplitude, shakeSeconds);

        // 火花爆在判定框此刻的世界中心上，压在角色之上，播完自己退场。
        var spark = new HitSpark { Heavy = reaction.IsHeavy, ZIndex = SparkZ };
        AddChild(spark);
        spark.GlobalPosition = _hitbox.GlobalPosition;

        _audio.PlayHit();
    }
}
