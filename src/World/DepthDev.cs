using Godot;
using Tinderhearth.Platform;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// `ENG-15` 纵深排序与代码影子的独立探针场景。
/// </summary>
/// <remarks>
/// 为什么另开一个场景而不是塞进 `PlayerDev` 或 `HitFeedbackDev`：本条要验的两件事都需要**专门
/// 摆位** —— 两个角色重叠着站在不同纵深上（验排序），以及一次干净的跳跃（验影子）。塞进那两个
/// 探针的连续时序里，摆位会踩到它们正在测的东西；而且这里要靠**截图数像素**判断谁压在谁上面，
/// 需要画面里没有别的东西在动。
///
/// **地面按纵深带画成 48px 厚。** 碰撞面对应带中线（`DepthRendering.DrawOffsetWorldPx` 的口径），
/// 所以视觉带从碰撞面上方 24px 到下方 24px。不这么画，角色在带内前后走时脚会离开那条画出来的
/// 地面线 —— 那不是排序错，是场景没把「地面是一条带」画出来。
/// </remarks>
public partial class DepthDev : Node2D
{
    /// <summary>碰撞地面的世界 Y。视觉带以它为中线上下各 24px。</summary>
    private const int GroundY = 200;

    /// <summary>两个角色重叠站位的 X。</summary>
    private const int MeetX = 200;

    /// <summary>验排序时主角与木桩的纵深，刻意只差 8px：错开得看得出，身体又大量重叠。</summary>
    private const int PlayerDepth = 20;
    private const int DummyDepth = 28;

    private static readonly Color DummyBody = new("b85450");

    private DepthSortedLayer _layer = null!;
    private PlayerActor _player = null!;
    private TrainingDummy _dummy = null!;
    private bool _probe;
    private int _tick;
    private int _passed;
    private int _checked;
    private bool _capturing;
    private int _drawn = -1;
    private int _tickSlips;
    private int _focusLost;

    // 影子累积量：跳跃全程逐帧核，不抽查一帧。
    private double _shadowGroundAtTakeoff = double.NaN;
    private bool _shadowGroundFixed = true;
    private bool _shadowShrank;
    private int _airFrames;
    private double _smallestScale = double.MaxValue;
    private int _redFront;
    private bool _jumping;

    public override void _Ready()
    {
        _probe = OS.GetCmdlineUserArgs().Contains("--eng15-probe");
        var router = new InputRouter();
        AddChild(router);

        // 地面：碰撞面在带中线，视觉是一条 48px 厚的带。
        var ground = new StaticBody2D { Position = new Vector2(320, GroundY) };
        ground.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(1200, 400) },
            Position = new Vector2(0, 200),
        });
        AddChild(ground);
        var half = DepthBand.WidthWorldPx / 2;
        AddChild(new Polygon2D
        {
            Polygon =
            [
                new(-600, GroundY - half), new(600, GroundY - half),
                new(600, GroundY + half), new(-600, GroundY + half),
            ],
            Position = new Vector2(320, 0),
            Color = new Color("485750"),
            ZIndex = -1,
        });

        _layer = new DepthSortedLayer();
        AddChild(_layer);
        _player = new PlayerActor { ManualPhysics = true, Position = new Vector2(MeetX, GroundY) };
        _player.Controllers.Assign(_player.ActorId, new LocalPlayerController(_player.ActorId) { Router = router });
        _layer.AddChild(_player);
        // 不带 `--probe` 时这就是给作者看的摆位：木桩错开一点、摆在带前沿，主角在带中线，于是
        // 一进场就看得出「谁在前、谁在后」与两个影子各自落在哪一排。探针模式下第 2 帧会把它挪回
        // 重叠位并改纵深（验排序要重叠），两种模式各自合理、互不迁就。
        _dummy = new TrainingDummy { Position = new Vector2(MeetX + 60, GroundY) };
        _layer.AddChild(_dummy);
        _dummy.PlaceDepth(DepthBand.FrontWorldPx);

        if (_probe)
        {
            Engine.MaxPhysicsStepsPerFrame = 1;
            return;
        }
        // 只有交互模式才加相机：探针靠世界坐标当屏幕坐标取像素，需要恒等变换。相机复用既有的
        // `GameCamera`（`check_camera.py` 盯着「派生 Camera2D 的类型恰好一个」，本条不新造）。
        AddChild(new GameCamera(CameraView.SideView) { FollowTarget = _player, Router = router });
    }

    /// <summary>窗口失焦次数。理由同 `GP-13`：失焦会让需要持续输入的判据以「玩法坏了」的形状失败。</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) _focusLost++;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_capturing) return;
        _tick++;
        if (_probe)
        {
            var drawn = (int)Engine.GetFramesDrawn();
            if (_drawn >= 0 && drawn == _drawn) _tickSlips++;
            _drawn = drawn;
        }
        _player.AdvanceCombat();
        _dummy.AdvanceCombat();
        _layer.Sort();
        if (!_probe) return;

        if (_jumping)
        {
            AdvanceJump();
            return;
        }
        if (_tick == 2)
        {
            // 验排序要两具身体重叠，所以把木桩从展示摆位挪回主角所在的 X。
            _dummy.Position = new Vector2(MeetX, GroundY);
            _player.Combat.Motor.PlaceDepth(PlayerDepth);
            _dummy.PlaceDepth(DummyDepth);
            return;
        }
        if (_tick == 3)
        {
            CheckStaticStage();
            BeginOverlapCapture();
            return;
        }
        if (_tick > 400) { Check("timeout", false); Capture(); }
    }

    /// <summary>静止阶段：覆盖量、排序方向、纵深偏移、贴地影子。</summary>
    private void CheckStaticStage()
    {
        // 守卫自报覆盖量：漏挂一个角色的表现是它不参与排序，而那不报错。
        Check("sorted-count", _layer.SortedCount == 2);

        // 纵深靠后的先画（z 小）。主角 20 在木桩 28 后面。
        Check("order-back-first", _player.ZIndex < _dummy.ZIndex
            && _player.ZIndex == DepthSortedLayer.ActorZBase
            && _dummy.ZIndex == DepthSortedLayer.ActorZBase + 1);

        // 纵深偏移：往前画得更低、往后画得更高，带中线为零。全部从规则层导出，探针不写死像素。
        Check("depth-offset-signed",
            Math.Abs(_player.Visual.Position.Y - DepthRendering.DrawOffsetWorldPx(PlayerDepth)) < 0.001
            && Math.Abs(_dummy.Visual.Position.Y - DepthRendering.DrawOffsetWorldPx(DummyDepth)) < 0.001
            && _player.Visual.Position.Y < 0 && _dummy.Visual.Position.Y > 0);

        // 贴地时影子与脚底重合。**两个角色一起核，一个纵深偏移为负、一个为正** —— 只核一个方向
        // 会漏掉「射线跟着绘制偏移走」那类缺陷：偏移为正时射线起点会落进地面碰撞体内部，量出来的
        // 地面也带上那次偏移，影子于是偏出去整整一个偏移量。2026-09-09 实测踩过这个形状。
        Check("shadow-foot-on-land",
            ShadowOnFoot(_player.Visual, _player) && ShadowOnFoot(_dummy.Visual, _dummy)
            && _player.Visual.Position.Y < 0 && _dummy.Visual.Position.Y > 0
            && Math.Abs(DepthRendering.ShadowScaleAt(0) - 1.0) < 1e-9);

        // 影子跟着纵深走：它画在角色所在那一排，所以它的全局 Y 也带着纵深偏移。同样两个方向都核。
        Check("shadow-follows-depth",
            Math.Abs(_player.Visual.GlobalPosition.Y - _player.GlobalPosition.Y
                - DepthRendering.DrawOffsetWorldPx(PlayerDepth)) < 0.001
            && Math.Abs(_dummy.Visual.GlobalPosition.Y - _dummy.GlobalPosition.Y
                - DepthRendering.DrawOffsetWorldPx(DummyDepth)) < 0.001);
    }

    /// <summary>影子落在宿主脚底那一点上：地面 Y 等于宿主的物理 Y，离地高度为零。</summary>
    private static bool ShadowOnFoot(DepthVisual visual, Node2D host) =>
        visual.GroundYWorldPx is double ground
        && Math.Abs(ground - host.GlobalPosition.Y) < 0.001
        && Math.Abs(visual.HeightAboveGroundWorldPx) < 0.001;

    /// <summary>数重叠区里木桩的红像素：靠前的那个可见面积必须更大。</summary>
    /// <remarks>
    /// **不信 <c>z_index</c> 自己报的数，去屏幕上数像素。** z_index 写对了但绘制顺序没跟着变
    /// （比如两个角色不在同一个父层下、或者被别的 z 值压过），排序判据照样全绿而画面上前后关系
    /// 是错的 —— 正典点名这条是承重项，所以要有一条真正看画面的判据。
    ///
    /// 取样矩形从两具身体的**几何**交集算出来，不写死：木桩 18 宽 32 高、主角碰撞框 18 宽 28 高，
    /// 各自的绘制位置由纵深偏移决定。
    /// </remarks>
    private async void BeginOverlapCapture()
    {
        _capturing = true;
        _redFront = await CountDummyRed();
        // 只交换两者的纵深，别的都不动。
        _player.Combat.Motor.PlaceDepth(DummyDepth);
        _dummy.PlaceDepth(PlayerDepth);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _player.Visual.Sync();
        _dummy.Visual.Sync();
        _layer.Sort();
        Check("order-swaps", _dummy.ZIndex < _player.ZIndex
            && _layer.SortedCount == 2);
        var redBack = await CountDummyRed();
        Check("overlap-front-visible", _redFront > 0 && redBack < _redFront);
        GD.Print($"[ENG15] dummyRed front={_redFront} back={redBack}");

        // 还原摆位，跳跃阶段用主角单独验影子。
        _player.Combat.Motor.PlaceDepth(DepthBand.CenterWorldPx);
        _dummy.PlaceDepth(DepthBand.FrontWorldPx);
        _dummy.Position = new Vector2(MeetX + 120, GroundY);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _tick = 0;
        _jumping = true;
        _capturing = false;
    }

    private async Task<int> CountDummyRed()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var scale = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
        // 两具身体在屏幕上的纵向区间（可视根已经带着各自的纵深偏移）；取交集再内缩 1px 避边。
        var playerBottom = _player.Visual.GlobalPosition.Y;
        var playerTop = playerBottom - 28;
        var dummyBottom = _dummy.Visual.GlobalPosition.Y;
        var dummyTop = dummyBottom - 32;
        var top = (int)Math.Ceiling(Math.Max(playerTop, dummyTop)) + 1;
        var bottom = (int)Math.Floor(Math.Min(playerBottom, dummyBottom)) - 1;
        var count = 0;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = MeetX - 8; x <= MeetX + 8; x++)
            {
                var pixel = image.GetPixel((int)(x * scale.X), (int)(y * scale.Y));
                if (Math.Abs(pixel.R - DummyBody.R) < 0.02 && Math.Abs(pixel.G - DummyBody.G) < 0.02
                    && Math.Abs(pixel.B - DummyBody.B) < 0.02)
                {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>跳跃阶段：影子的地面位置全程不变，且在空中缩小、落地恢复。</summary>
    private void AdvanceJump()
    {
        var visual = _player.Visual;
        if (_tick == 3)
        {
            _shadowGroundAtTakeoff = visual.GroundYWorldPx ?? double.NaN;
            Press(InputActions.Jump, true);
            return;
        }
        if (_tick == 4) Press(InputActions.Jump, false);
        if (_tick < 4) return;

        if (!_player.IsOnFloor())
        {
            _airFrames++;
            var scale = DepthRendering.ShadowScaleAt(visual.HeightAboveGroundWorldPx);
            _smallestScale = Math.Min(_smallestScale, scale);
            _shadowShrank |= scale < 1.0 - 1e-9;
            // 地面位置全程不变：影子留在地面，不随角色升高。
            _shadowGroundFixed &= visual.GroundYWorldPx is double ground
                && Math.Abs(ground - _shadowGroundAtTakeoff) < 0.001
                && visual.HeightAboveGroundWorldPx > 0;
            return;
        }
        if (_airFrames == 0) return;

        // 滞空帧数下限由跳跃手感常量导出，只用来排除「压根没起跳也算过」。
        var risingFrames = CombatFeel.JumpInitialPixelsPerSecond
            / (double)CombatFeel.GravityPixelsPerSecondSquared * CombatFeel.PhysicsTicksPerSecond;
        Check("shadow-ground-fixed", _shadowGroundFixed && _airFrames >= risingFrames
            && !double.IsNaN(_shadowGroundAtTakeoff));
        // 峰高约等于缩到底的高度，所以最小缩放该贴着登记的下限；这里只要求它真的缩过且没越界。
        Check("shadow-shrinks-in-air", _shadowShrank
            && _smallestScale >= CombatFeel.ShadowMinScalePercent / 100.0 - 1e-9
            && _smallestScale < 1.0);
        // 落地静止后离地高度不是**精确**的 0：`CharacterBody2D` 的碰撞安全边距（`SafeMargin`，
        // 引擎默认 0.08）留了一丝间隙，实测 0.025px。所以容差取引擎自报的那个边距（踩坑记录 49），
        // 而「恢复了」这件事按**可观察量**判：影子取整后的宽度回到贴地原宽，而不是要求缩放位
        // 精确等于 1.0 —— 那个要求会被一个看不见的 0.0004 判失败。
        var landedWidth = Math.Round(
            CombatFeel.ShadowWidthWorldPx * DepthRendering.ShadowScaleAt(visual.HeightAboveGroundWorldPx));
        Check("shadow-restored-on-land",
            visual.HeightAboveGroundWorldPx <= _player.SafeMargin
            && landedWidth == CombatFeel.ShadowWidthWorldPx);
        GD.Print($"[ENG15] airFrames={_airFrames} smallestScale={_smallestScale:F3}");
        Capture();
    }

    private static void Press(string action, bool pressed) => Callable.From(() =>
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed })).CallDeferred();

    private void Check(string name, bool value)
    {
        _checked++;
        if (value) _passed++;
        GD.Print($"[ENG15] {(value ? "PASS" : "FAIL")} {name}");
    }

    private async void Capture()
    {
        _capturing = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        Check("screenshot", image.GetWidth() > 0
            && image.SavePng(OS.GetEnvironment("ENG15_SHOT")) == Error.Ok);
        GD.Print($"[ENG15] tickSlips={_tickSlips} focusLost={_focusLost}");
        Check("tick-frame-1to1", _tickSlips == 0);
        Check("focus-kept", _focusLost == 0);
        GD.Print($"[ENG15] Summary {_passed}/{_checked}");
        GetTree().Quit(_passed == _checked ? 0 : 1);
    }
}
