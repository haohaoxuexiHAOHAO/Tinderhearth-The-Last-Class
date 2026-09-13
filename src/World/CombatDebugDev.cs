using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// `ENG-6` 帧调优叠层（<see cref="CombatDebugOverlay"/>）的独立探针场景与交互展示。
/// </summary>
/// <remarks>
/// 为什么另开一个场景而不塞进 `HitFeedbackDev`：帧步进要**自己掌控推进节奏**（暂停时不推、单步时
/// 推一帧），而 `HitFeedbackDev._PhysicsProcess` 是一条密集的固定时序，两者会打架（同 `DepthDev`
/// 另开的理由）。这里主角 `ManualPhysics=true`，推进由本场景的循环调 —— 每帧问
/// <see cref="CombatDebugOverlay.ShouldAdvance"/> 决定推不推，正是被测的那件事。
///
/// 交互模式（不带 `--probe`）给作者看：叠层默认开着（框可见），A/D 走、J/K 出招，`V` 开关叠层、
/// `P` 暂停/继续、`.` 单步。探针模式验两组：判定框/受击框叠层的几何对不对、帧步进真的冻结与单推。
/// </remarks>
public partial class CombatDebugDev : Node2D
{
    private const int GroundY = 200;
    private const int PlayerX = 200;

    /// <summary>木桩「在轻击框内」的放置距离：两个 18 宽实体刚好不互插的最小间距（同 `HitFeedbackDev`）。</summary>
    private const int NearX = Hurtbox.WidthWorldPx;

    /// <summary>木桩「够不到」的放置距离，验判定框可视化时用，免得顺手打中干扰。</summary>
    private const int FarX = 120;

    private InputRouter _router = null!;
    private PlayerActor _player = null!;
    private TrainingDummy _dummy = null!;
    private Hitbox _hitbox = null!;
    private CombatDebugOverlay _overlay = null!;
    private readonly Hitstop _stop = new();

    private bool _probe;
    private int _stage;
    private int _tick;
    private int _passed;
    private int _checked;
    private bool _capturing;
    private int _drawn = -1;
    private int _tickSlips;
    private int _focusLost;

    // 帧步进记账。
    private bool _hiddenChecked;
    private int _frozenFip;
    private bool _frozenOk = true;
    private int _stepFipBefore;
    private bool _stepAdvanced;
    private int _fipAfterStep;

    public override void _Ready()
    {
        _probe = OS.GetCmdlineUserArgs().Contains("--eng6-probe");
        _router = new InputRouter();
        AddChild(_router);

        var ground = new StaticBody2D { Position = new Vector2(320, GroundY) };
        ground.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(1200, 40) },
            Position = new Vector2(0, 20),
        });
        ground.AddChild(new Polygon2D
        {
            Polygon = [new(-600, 0), new(600, 0), new(600, 40), new(-600, 40)],
            Color = new Color("485750"),
        });
        AddChild(ground);

        _player = new PlayerActor { ManualPhysics = true, Position = new Vector2(PlayerX, GroundY) };
        _player.Controllers.Assign(_player.ActorId,
            new LocalPlayerController(_player.ActorId) { Router = _router });
        AddChild(_player);
        var playerHurt = new Hurtbox { Actor = _player, HeightWorldPx = PlayerActor.BodyHeightWorldPx };
        _player.AddChild(playerHurt);
        _hitbox = new Hitbox();
        _player.AddChild(_hitbox);

        _dummy = new TrainingDummy { Position = new Vector2(PlayerX + NearX, GroundY) };
        AddChild(_dummy);

        _overlay = new CombatDebugOverlay { Hitbox = _hitbox, Hurtboxes = [playerHurt, _dummy.Hurtbox] };
        AddChild(_overlay);

        if (_probe)
        {
            // 与其它探针同一前提：一次迭代只准一个物理帧，好让 deferred 注入的输入与逐帧断言对齐。
            Engine.MaxPhysicsStepsPerFrame = 1;
            return;
        }

        // 交互模式：叠层默认开着，框一进场就可见；相机复用既有 GameCamera（check_camera 盯着
        // 「派生 Camera2D 的类型恰好一个」，本场景不新造）。
        _overlay.Enabled = true;
        AddChild(new GameCamera(CameraView.SideView) { FollowTarget = _player, Router = _router });
    }

    /// <summary>窗口失焦次数。理由同 `GP-13`：失焦会让需要持续输入的判据以「玩法坏了」的形状失败。</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            _focusLost++;
        }
    }

    /// <summary>交互调试键。这几个键**不过 InputMap**，登记在 `check_input_map.py` 的 `HARNESS_KEYS`。</summary>
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
        if (_capturing)
        {
            return;
        }

        _tick++;
        if (_probe)
        {
            var drawn = (int)Engine.GetFramesDrawn();
            if (_drawn >= 0 && drawn == _drawn)
            {
                _tickSlips++;
            }

            _drawn = drawn;
        }

        if (!_probe)
        {
            // 交互推进：帧步进与顿帧共用一个 gate，暂停或顿帧时当帧不推进。
            if (_overlay.ShouldAdvance() && !_stop.Tick())
            {
                _player.AdvanceCombat();
                _dummy.AdvanceCombat();
                _hitbox.Resolve(_player, OnHit);
            }

            return;
        }

        RunStage();
    }

    /// <summary>推进一战斗帧，除非暂停（帧步进）或顿帧冻结。返回是否真的推进了。</summary>
    private bool TryAdvance()
    {
        if (!_overlay.ShouldAdvance() || _stop.Tick())
        {
            return false;
        }

        _player.AdvanceCombat();
        _dummy.AdvanceCombat();
        _hitbox.Resolve(_player, OnHit);
        return true;
    }

    private void OnHit(HitReaction reaction) => _stop.Begin(reaction.HitstopFrames);

    private void RunStage()
    {
        var combo = _player.Combat.Combo;
        switch (_stage)
        {
            case 0: // 可视化：连续推进（先落地、暖输入），第 10 帧起手一次轻击，验叠层画的框。
                if (_tick == 1)
                {
                    Check("overlay-default-off", !_overlay.Enabled && _overlay.ShouldAdvance());
                    _overlay.Enabled = true;
                    _dummy.Position = new Vector2(PlayerX + FarX, GroundY);
                    CheckHurtboxFollowsDepth();
                }

                TryAdvance();
                if (_tick == 10)
                {
                    Press(InputActions.AttackLight, true);
                }
                else if (_tick == 11)
                {
                    Press(InputActions.AttackLight, false);
                }

                // 前摇帧（Active 之前）：判定框未开，叠层不该画判定框。只查一次。
                if (!_hiddenChecked && combo.IsAttacking && combo.Phase == AttackPhase.Startup)
                {
                    Check("hitbox-viz-hidden-inactive", _overlay.CurrentHitboxWorld is null);
                    _hiddenChecked = true;
                }

                // Active 帧：叠层画的判定框 ≙ 独立按 SpecFor 重算的框；受击框 ≙ 角色几何。
                if (combo.IsHitActive)
                {
                    var spec = Hitbox.SpecFor(combo.Kind, combo.Step);
                    var center = _player.GlobalPosition
                        + new Vector2(_player.Combat.Motor.Facing * spec.Size.X / 2f, -spec.CenterY);
                    var expected = new Rect2(center - spec.Size / 2f, spec.Size);
                    Check("hitbox-viz-matches-active",
                        _overlay.CurrentHitboxWorld is Rect2 got && RectClose(got, expected));
                    Check("hurtbox-viz-matches", HurtboxVizOk());
                    Stage(1);
                }
                else if (_tick > 60)
                {
                    if (!_hiddenChecked)
                    {
                        Check("hitbox-viz-hidden-inactive", false);
                    }

                    Check("hitbox-viz-matches-active", false);
                    Check("hurtbox-viz-matches", false);
                    Capture();
                }

                break;

            case 1: // 让这次挥击跑完回到待机，再把木桩挪近、起手下一次轻击供帧步进验。
                if (combo.IsAttacking)
                {
                    TryAdvance();
                    break;
                }

                _dummy.Position = new Vector2(PlayerX + NearX, GroundY);
                Press(InputActions.AttackLight, true);
                Stage(2);
                break;

            case 2: // 未暂停地把攻击推进到前摇中（避免输入边沿跨暂停丢失），再暂停。
                if (_tick == 1)
                {
                    Press(InputActions.AttackLight, false);
                }

                TryAdvance();
                if (combo.IsAttacking && combo.Phase == AttackPhase.Startup && combo.FrameInPhase >= 1)
                {
                    _overlay.Paused = true;
                    _frozenFip = combo.FrameInPhase;
                    _frozenOk = true;
                    Stage(3);
                }
                else if (_tick > 30)
                {
                    Check("paused-freezes-combat", false);
                    Capture();
                }

                break;

            case 3: // 暂停：连续几帧都不推进，进行中的攻击帧号冻住。
                _frozenOk &= !TryAdvance() && combo.FrameInPhase == _frozenFip;
                if (_tick >= 4)
                {
                    Check("paused-freezes-combat", _frozenOk);
                    Stage(4);
                }

                break;

            case 4: // 单步：请求一次 → 恰好推进一帧；下一帧不再自行推进。
                if (_tick == 1)
                {
                    _stepFipBefore = combo.FrameInPhase;
                    _overlay.RequestStep();
                    _stepAdvanced = TryAdvance();
                    _fipAfterStep = combo.FrameInPhase;
                }
                else if (_tick == 2)
                {
                    var again = TryAdvance();
                    Check("step-advances-exactly-one",
                        _stepAdvanced && _fipAfterStep == _stepFipBefore + 1
                        && !again && combo.FrameInPhase == _fipAfterStep);
                    Stage(5);
                }

                break;

            case 5: // 一步步推到 Active 命中木桩：证明单步跑的是同一份结算（命中、硬直帧都对）。
                _overlay.RequestStep();
                TryAdvance();
                if (_dummy.HitCount > 0)
                {
                    Check("step-runs-full-resolution",
                        _dummy.HitCount == 1
                        && _dummy.Statuses.Get(StatusKind.Hitstun).RemainingFrames
                            == CombatFeel.LightHitstunFrames);
                    Capture();
                }
                else if (_tick > 60)
                {
                    Check("step-runs-full-resolution", false);
                    Capture();
                }

                break;
        }
    }

    /// <summary>
    /// 叠层的受击框跟着角色精灵的纵深绘制偏移一起挪（`GP-14` 阶段 1 实机发现的 bug 的执行体）。
    /// </summary>
    /// <remarks>
    /// 别的判据都在带中线（默认纵深）验，那里绘制偏移恰好为零、验不出这条 —— 作者正是把主角往纵深里
    /// 走之后才看见受击框飘在头顶。所以这里专门把主角摆到带前沿（偏移非零），核受击框下沿跟着精灵偏
    /// 移了同样一段；用真实缺陷形状反证：叠层若不补偏移（改动前的写法），框停在物理脚底、这条当场变红。
    /// 检查完立刻还原纵深，不影响后面帧步进那几个阶段（它们要在带中线上跑）。
    /// </remarks>
    private void CheckHurtboxFollowsDepth()
    {
        var restore = _player.Combat.Motor.DepthWorldPx;
        _player.Combat.Motor.PlaceDepth(DepthBand.FrontWorldPx);
        _player.Visual.Sync();

        var offset = DepthRendering.DrawOffsetWorldPx(DepthBand.FrontWorldPx);
        var rect = _overlay.CurrentHurtboxesWorld().First();
        // 期望：受击框下沿 = 主角物理脚底 + 纵深绘制偏移（精灵也偏了同样一段）；高度仍是本体 28。
        Check("hurtbox-viz-follows-depth",
            Math.Abs(offset) > 0.001                                        // 前沿偏移确非零，否则空验
            && Math.Abs(rect.End.Y - (_player.GlobalPosition.Y + offset)) < 0.01f
            && Math.Abs(rect.Size.Y - PlayerActor.BodyHeightWorldPx) < 0.01f);

        _player.Combat.Motor.PlaceDepth(restore);
        _player.Visual.Sync();
    }

    /// <summary>叠层的受击框 ≙ 角色实际几何：18×32、下沿贴脚底、以角色 X 居中。玩家与木桩各一个。</summary>
    /// <summary>
    /// 叠层的受击框 ≙ 角色实际几何：18 宽、下沿贴脚底、以角色 X 居中，**高度按角色各自的实测本体**。
    /// </summary>
    /// <remarks>
    /// 高度**逐角色分别核**（2026-09-12）：主角取 <see cref="PlayerActor.BodyHeightWorldPx"/>（站立姿
    /// 实测 28），木桩取它自己的柱子高（32）。原先两边共用一个写死的 32，主角因此高出 4px —— 从头顶
    /// 掠过的攻击照样命中且不报错。判据跟着改成两个不同的期望值，**共用一个数就通不过**，于是「谁把
    /// 它们又并成一个」会当场变红。
    /// </remarks>
    private bool HurtboxVizOk()
    {
        var rects = _overlay.CurrentHurtboxesWorld().ToList();
        return rects.Count == 2
            && HurtOk(rects[0], _player, PlayerActor.BodyHeightWorldPx)
            && HurtOk(rects[1], _dummy, _dummy.Hurtbox.HeightWorldPx)
            // 两个高度必须真的不同：相等就说明又回到「一个数套所有角色」，而画面上它们本来不一样高。
            && PlayerActor.BodyHeightWorldPx != _dummy.Hurtbox.HeightWorldPx;
    }

    private static bool HurtOk(Rect2 rect, Node2D actor, int expectHeight) =>
        Math.Abs(rect.Size.X - Hurtbox.WidthWorldPx) < 0.01f
        && Math.Abs(rect.Size.Y - expectHeight) < 0.01f
        && Math.Abs(rect.Position.X + rect.Size.X / 2f - actor.GlobalPosition.X) < 0.01f
        && Math.Abs(rect.End.Y - actor.GlobalPosition.Y) < 0.01f;

    private static bool RectClose(Rect2 a, Rect2 b) =>
        a.Position.DistanceTo(b.Position) < 0.01f && a.Size.DistanceTo(b.Size) < 0.01f;

    private void Stage(int stage)
    {
        _stage = stage;
        _tick = 0;
    }

    private static void Press(string action, bool pressed) => Callable.From(() =>
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed })).CallDeferred();

    private void Check(string name, bool value)
    {
        _checked++;
        if (value)
        {
            _passed++;
        }

        GD.Print($"[ENG6] {(value ? "PASS" : "FAIL")} {name}");
    }

    private async void Capture()
    {
        _capturing = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        Check("screenshot", image.GetWidth() > 0
            && image.SavePng(OS.GetEnvironment("ENG6_SHOT")) == Error.Ok);
        GD.Print($"[ENG6] tickSlips={_tickSlips} focusLost={_focusLost}");
        Check("tick-frame-1to1", _tickSlips == 0);
        Check("focus-kept", _focusLost == 0);
        GD.Print($"[ENG6] Summary {_passed}/{_checked}");
        GetTree().Quit(_passed == _checked ? 0 : 1);
    }
}
