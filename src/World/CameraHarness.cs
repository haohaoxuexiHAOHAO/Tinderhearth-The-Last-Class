using Godot;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// 相机的**验收脚手架**：一片有边界的地面、一个能走的角色、几个调试键（`UI-5` 的实机确认）。
/// </summary>
/// <remarks>
/// **为什么必须有它**：`UI-5` 的机器判据全绿了，但验收表里「作者实机确认」那条没有执行它的
/// 办法 —— 场景里没有地面也没有能走的角色，死区取多大、震动会不会晕、推镜读不读得出是提示，
/// 这些只能人判的东西无从判起。这个类补的就是那个缺口，同时也是 `UI-12`（相机手感实机校准）
/// 收敛那六个数唯一的工具。
///
/// **它不是基地场景，也不是关卡。** 场景与关卡设计归经营侧与战斗侧的实现需求。这里只摆最少的
/// 东西让相机行为看得见：可拼接的地面（看得出镜头在动）、画出来的地图边界与可建造区边界
/// （看得出钳制与推镜在什么位置触发）、每 5 格一个地标（数得出镜头移了多远）。
///
/// **`UI-8` 的 HUD 加在这上面**，`UI-10` 的端到端测试会替换它 —— 与 <see cref="CameraProbe"/>、
/// <c>Main.ProbeUiSkeleton</c> 同一性质。
///
/// 两条实现取舍写在这里，免得看起来像绕守卫：
///
/// **调试键走 <see cref="Node._UnhandledKeyInput"/> 事件，不进正式绑定表。** `UI-7` 那条约定
/// 禁的是**轮询**（实测 <c>SetInputAsHandled</c> 不清轮询状态，所以轮询会打出没打算打的动作），
/// 事件驱动不在禁区内。不进绑定表是因为这些键不是玩法输入，混进去会让绑定表的条数判据跟着漂，
/// 而那张表是「将来完全重映射」的权威源。**角色移动仍走 <see cref="InputRouter"/>。**
///
/// **移动速度是脚手架取值，不是玩法数值。** 玩法数值的设计在设计仓 `design/数值模型.md`，
/// 把它搬进规则层属各玩法实现需求。这里要的只是「走起来能看出镜头跟不跟手」。
/// </remarks>
public sealed partial class CameraHarness : Node2D
{
    /// <summary>角色移动速度，世界像素／秒。**脚手架取值** —— 见类注释。</summary>
    private const float WalkPixelsPerSecond = 96f;

    /// <summary>地图比可建造区每边多出的格数。留出这一圈，钳制与推镜才有区别可看。</summary>
    private const int MapMarginCells = 8;

    /// <summary>地标间隔，格。取 5 格是因为 5×16 ＝ 80 世界像素，侧视视野正好装得下 4 个。</summary>
    private const int LandmarkEveryCells = 5;

    /// <summary>侍武士精灵表的帧宽高（登记表里的规格），与帧率。</summary>
    private const int SpriteFrame = 96;
    private const double SpriteFps = 12.0;

    /// <summary>存一张图然后退出。用法：<c>&lt;godot&gt; --path . -- --harness-shot</c>。</summary>
    private const string ShotArg = "--harness-shot";

    // 占位色。**正式界面与场景色板归 `DOC-2`**，这里只求几条线互相分得开。
    private static readonly Color MapEdgeColor = Color.Color8(0xE0, 0x5A, 0x3A);        // 地图边界：红
    private static readonly Color BuildableEdgeColor = Color.Color8(0x5A, 0xC0, 0xE0);  // 可建造区：蓝
    private static readonly Color PushBandColor = Color.Color8(0xE0, 0xC0, 0x40);       // 推镜触发带：黄
    private static readonly Color GridMajorColor = Color.Color8(0x6B, 0x52, 0x3C);      // 20 格粗线
    private static readonly Color GridMinorColor = Color.Color8(0x88, 0x78, 0x60);      // 5 格细线

    private readonly GameConfig _config;
    private readonly UiRoot _ui;
    private readonly InputRouter _router;

    private GameCamera _camera = null!;
    private Sprite2D _actor = null!;
    private Sprite2D _ground = null!;
    private Label _overlay = null!;

    private CameraView _view = CameraView.TopDown;
    private CameraCutscene? _cutscene;
    private double _cutsceneLeft;
    private double _animTime;
    private bool _moving;
    private int _shotCountdown = -1;

    private Texture2D _idleSheet = null!;
    private Texture2D _runSheet = null!;
    private int _idleFrames;
    private int _runFrames;

    public CameraHarness(GameConfig config, UiRoot ui, InputRouter router)
    {
        _config = config;
        _ui = ui;
        _router = router;
    }

    private int BuildableWidthPx => _config.BuildableWidthCells * UiMetrics.BaseUnit;

    private int BuildableHeightPx => _config.BuildableHeightCells * UiMetrics.BaseUnit;

    private int MarginPx => MapMarginCells * UiMetrics.BaseUnit;

    public override void _Ready()
    {
        // 手环占位面板铺满屏幕且现在没有内容，留着会挡住要看的东西。真 HUD 是 `UI-8` 的事。
        _ui.Close(Wristband.Surface);

        BuildGround();
        BuildLandmarks();
        BuildActor();
        BuildCamera();
        BuildOverlay();

        GD.Print("[脚手架] 相机验收场景就绪｜地图 ",
                 BuildableWidthPx + (MarginPx * 2), "x", BuildableHeightPx + (MarginPx * 2),
                 "px｜可建造区 ", BuildableWidthPx, "x", BuildableHeightPx,
                 "px（", _config.BuildableWidthCells, "x", _config.BuildableHeightCells, " 格）");
        GD.Print("[脚手架] 调试键 F1 切视角｜F2 切建造模式｜F3 震一下｜F4 震动开关｜"
                 + "F9 放一段演出｜F10 打印当前数值");

        if (OS.GetCmdlineUserArgs().Contains(ShotArg))
        {
            _shotCountdown = 8;     // 等布局与首帧稳定，理由与 Main 延后两帧打显示指标相同
        }
    }

    public override void _Process(double delta)
    {
        TickCutscene(delta);
        MoveActor(delta);
        RefreshOverlay();
        QueueRedraw();
        TickShot();
    }

    /// <summary>
    /// 存一张图然后退出（<c>-- --harness-shot</c>）。
    /// </summary>
    /// <remarks>
    /// **给看不见屏幕的人用的。** 排 HUD 与判断脚手架有没有真画出东西时，「跑起来看一眼」这件事
    /// 本身需要一个能落成文件的形式；靠人转述「好像是空的」既慢又不可复核。`UI-8` 排版会反复用它。
    /// 与 <see cref="CameraProbe"/> 里那次截图同一约束：headless 下取不到视口纹理，所以本项只在
    /// 有渲染设备时才可能被触发（脚手架本身在 headless 下就不建）。
    /// </remarks>
    private void TickShot()
    {
        if (_shotCountdown < 0 || --_shotCountdown > 0)
        {
            return;
        }

        Shoot();

        // 两种视角各存一张。只存俯视那张的话侧视的 2 倍取景与侍武士精灵表就没人看过，
        // 而「两种视角共用同一份实现」正是本条的立项理由 —— 只看一半等于没看。
        if (_view == CameraView.TopDown)
        {
            ToggleView();
            _shotCountdown = 8;
            return;
        }

        _shotCountdown = -1;
        GetTree().Quit(0);
    }

    private void Shoot()
    {
        var dir = ProjectSettings.GlobalizePath("res://logs/art");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var stamp = Time.GetDatetimeStringFromSystem()
            .Replace(":", string.Empty).Replace("-", string.Empty).Replace("T", "-");
        var path = $"{dir}/harness-{_view}-{stamp}.png";
        var image = GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        GD.Print("[脚手架] 存图 ", path, "（", image.GetWidth(), "x", image.GetHeight(),
                 " 物理像素，视角 ", _view, "，错误码 ", err, "）");
    }

    /// <summary>
    /// 画出**看不见就没法验收**的三样东西：地图边界、可建造区边界、推镜触发带。
    /// </summary>
    /// <remarks>
    /// 边界不画出来的话「视口越界了没有」只能靠猜；推镜触发带不画出来的话，镜头动起来分不清是
    /// 死区跟随还是推镜。线宽固定 1 世界像素 —— 侧视 2 倍下它是 2 物理像素，与素材同一网格。
    /// </remarks>
    public override void _Draw()
    {
        var map = new Rect2(-MarginPx, -MarginPx,
            BuildableWidthPx + (MarginPx * 2), BuildableHeightPx + (MarginPx * 2));
        var buildable = new Rect2(0, 0, BuildableWidthPx, BuildableHeightPx);
        var band = buildable.Grow(-CameraFeel.EdgePushMarginPixels);

        DrawGrid(map);
        DrawRect(band, PushBandColor, filled: false, width: 1f);
        DrawRect(buildable, BuildableEdgeColor, filled: false, width: 2f);
        DrawRect(map, MapEdgeColor, filled: false, width: 2f);
    }

    /// <summary>
    /// 画一张格线网。**这不是装饰，是量具** —— 没有它横向移动看不出来。
    /// </summary>
    /// <remarks>
    /// 占位地面图块只有上边缘一道深色，平铺出来是横条纹：竖直移动看得见，水平移动几乎看不见，
    /// 而侧视关卡里主要的移动方向恰好是水平的（2026-08-31 存图看出来的，不是推断）。
    /// 所以格线自己画：细线每 5 格、粗线每 20 格，于是镜头移了多远**数得出来**而不是「感觉动了」。
    /// 刻意不做成素材：它是脚手架的量具，不该占登记表里的槽位。
    /// </remarks>
    private void DrawGrid(Rect2 map)
    {
        var minor = LandmarkEveryCells * UiMetrics.BaseUnit;
        var major = minor * 4;
        var x0 = (int)map.Position.X;
        var y0 = (int)map.Position.Y;
        var x1 = (int)map.End.X;
        var y1 = (int)map.End.Y;

        for (var x = x0; x <= x1; x += minor)
        {
            var heavy = (x - x0) % major == 0;
            DrawLine(new Vector2(x, y0), new Vector2(x, y1),
                heavy ? GridMajorColor : GridMinorColor, 1f);
        }

        for (var y = y0; y <= y1; y += minor)
        {
            var heavy = (y - y0) % major == 0;
            DrawLine(new Vector2(x0, y), new Vector2(x1, y),
                heavy ? GridMajorColor : GridMinorColor, 1f);
        }
    }

    // ── 搭场景 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 铺地面。用 <c>RegionEnabled</c> 加 <c>TextureRepeat</c> 平铺一张 16×16 图块。
    /// </summary>
    /// <remarks>
    /// 不用 1,200 个 <c>Sprite2D</c>：那是给 <c>TileMap</c> 干的活，而关卡与基地的图块摆放归
    /// 各自的实现需求，本脚手架只需要「看得出镜头在动」的纹理。
    /// **不碰 <c>TextureFilter</c>** —— 项目级最近邻是 12px 中文清晰的唯一依靠（`UI-4` 实测）。
    /// </remarks>
    private void BuildGround()
    {
        var tile = LoadTexture("res://assets/placeholder/tiles/ground.png");
        var map = new Rect2(-MarginPx, -MarginPx,
            BuildableWidthPx + (MarginPx * 2), BuildableHeightPx + (MarginPx * 2));

        _ground = new Sprite2D
        {
            Name = "Ground",
            Texture = tile,
            Centered = false,
            RegionEnabled = true,
            RegionRect = new Rect2(0, 0, map.Size.X, map.Size.Y),
            Position = map.Position,
            TextureRepeat = TextureRepeatEnum.Enabled,
            ZIndex = -100,
        };
        AddChild(_ground);
    }

    /// <summary>
    /// 摆一批 32×32 的剪影当参照物。
    /// </summary>
    /// <remarks>
    /// 两个用处，都不是装饰：一是**数得出镜头移了多远**（格线给刻度，剪影给可辨认的目标）；
    /// 二是 `UI-8` 有一条验收要求「同屏 10–15 个敌人时 HUD 不遮挡角色所在的可读区」，
    /// 那时需要屏幕上真有那么多个。所以数量按那条取 15 个，格对齐摆开。
    /// </remarks>
    private void BuildLandmarks()
    {
        var enemy = LoadTexture("res://assets/placeholder/chars/enemy.png");
        var ally = LoadTexture("res://assets/placeholder/chars/ally.png");
        var holder = new Node2D { Name = "Landmarks", ZIndex = -90 };
        AddChild(holder);

        // 5 列 × 3 行 ＝ 15 个，横向每 8 格、纵向每 8 格，全部落在格心。
        var step = 8 * UiMetrics.BaseUnit;
        for (var col = 0; col < 5; col++)
        {
            for (var row = 0; row < 3; row++)
            {
                holder.AddChild(new Sprite2D
                {
                    Texture = (col + row) % 3 == 0 ? ally : enemy,
                    Centered = true,
                    Position = new Vector2(
                        (col * step) + (step / 2), (row * step) + (step / 2)),
                });
            }
        }
    }

    /// <summary>
    /// 摆角色。侧视用侍武士精灵表，俯视用占位剪影。
    /// </summary>
    /// <remarks>
    /// 两种视角用不同素材是**素材的事实**而不是相机的分支：侍武士那套是侧面画的，俯视 45 度
    /// 用它会明显不对。相机对这件事一无所知 —— 它只跟一个 <see cref="Node2D"/>。
    /// </remarks>
    private void BuildActor()
    {
        _idleSheet = LoadTexture("res://assets/downloaded/samurai/idle.png");
        _runSheet = LoadTexture("res://assets/downloaded/samurai/run.png");
        _idleFrames = (int)(_idleSheet.GetWidth() / SpriteFrame);
        _runFrames = (int)(_runSheet.GetWidth() / SpriteFrame);

        _actor = new Sprite2D
        {
            Name = "Actor",
            Centered = true,
            Position = new Vector2(BuildableWidthPx / 2, BuildableHeightPx / 2),
            RegionEnabled = true,
            RegionRect = new Rect2(0, 0, SpriteFrame, SpriteFrame),
        };
        AddChild(_actor);
        ApplyActorLook();
    }

    private void BuildCamera()
    {
        _camera = new GameCamera(_view)
        {
            FollowTarget = _actor,
            Router = _router,
        };
        AddChild(_camera);          // 挂在本节点下：父先子后，角色先动、相机后跟，不差一帧

        ApplyBounds();
        _camera.Rig.SnapTo((int)_actor.Position.X, (int)_actor.Position.Y);
        _camera.Apply();
    }

    /// <summary>
    /// 屏幕上打一行当前状态。**用引擎默认字体** —— 像素字体与 <c>Theme</c> 是 `UI-8` 的第一步。
    /// </summary>
    private void BuildOverlay()
    {
        _overlay = new Label
        {
            Name = "HarnessOverlay",
            Text = "",
        };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _overlay.Position = new Vector2(UiMetrics.SafeMargin, UiMetrics.SafeMargin);

        // 字号压到 8：默认字号在 640×360 的逻辑分辨率下一行占近 20px，五行就吃掉画面的三分之一
        // （2026-08-31 存图看出来的）。这里要看的是镜头，读数只是旁注。
        // 引擎默认字体在 8px 下不锐利 —— **这是刻意接受的**，像素字体与 `Theme` 是 `UI-8` 的第一步。
        _overlay.AddThemeFontSizeOverride("font_size", UiMetrics.Grid);
        _ui.LayerOf(UiLayer.Hud).AddChild(_overlay);
    }

    // ── 每帧 ────────────────────────────────────────────────────────────

    private void MoveActor(double delta)
    {
        // 输入经门面问，不直接轮询引擎（`UI-7` 的约定，`check_input_map.py` 扫 src/ 判失败）。
        var axis = _router.MoveDirection();

        // 建造模式下方向轴给相机滚动用，角色站着不动 —— 真正的建造界面要决定这个轴是驱动格子
        // 光标还是驱动镜头（`PanelNavigation` 的注释倾向跟着光标），那是建造实现的事。
        _moving = !_camera.BuildMode && axis != Vector2.Zero;
        if (_moving)
        {
            var step = axis.Normalized() * WalkPixelsPerSecond * (float)delta;
            var next = _actor.Position + step;

            // 钳的是角色**身体**而不是中心点：只钳中心的话精灵有一半会越过红线，看起来像
            // 「角色跑出地图了」，而那正是本脚手架要让人一眼看清的东西（2026-08-31 作者实机指出）。
            // 退让一个基础单位 —— 32px 精灵格的一半。
            var inset = MarginPx - UiMetrics.BaseUnit;
            _actor.Position = new Vector2(
                Mathf.Clamp(next.X, -inset, BuildableWidthPx + inset),
                Mathf.Clamp(next.Y, -inset, BuildableHeightPx + inset));
            if (!Mathf.IsZeroApprox(axis.X))
            {
                _actor.FlipH = axis.X < 0;
            }
        }

        AdvanceAnimation(delta);
    }

    private void AdvanceAnimation(double delta)
    {
        if (_view != CameraView.SideView)
        {
            return;         // 俯视用单帧剪影，没有动画
        }

        _animTime += delta;
        var frames = _moving ? _runFrames : _idleFrames;
        var frame = frames <= 0 ? 0 : (int)(_animTime * SpriteFps) % frames;
        _actor.Texture = _moving ? _runSheet : _idleSheet;
        _actor.RegionRect = new Rect2(frame * SpriteFrame, 0, SpriteFrame, SpriteFrame);
    }

    private void TickCutscene(double delta)
    {
        if (_cutscene is null)
        {
            return;
        }

        _cutsceneLeft -= delta;
        if (_cutsceneLeft > 0.0)
        {
            return;
        }

        _cutscene.Release();
        _cutscene = null;
        GD.Print("[脚手架] 演出归还相机 —— 跟随行为应与接管前一致，下一帧直接对准角色");
    }

    private void RefreshOverlay()
    {
        var rig = _camera.Rig;
        _overlay.Text =
            $"{_view} x{rig.Zoom} 视野 {rig.VisibleWidth}x{rig.VisibleHeight}"
            + $"　{(_camera.BuildMode ? "建造·方向键滚镜头" : "行走·方向键走人")}"
            + $"　死区 {rig.DeadzoneHalfWidth}x{rig.DeadzoneHalfHeight}"
            + $"　震动 {(rig.ShakeEnabled ? "开" : "关")}"
            + $" {CameraFeel.ShakeAmplitudeScreenPx}px/{CameraFeel.ShakeSeconds}s\n"
            + $"角色 {(int)_actor.Position.X},{(int)_actor.Position.Y}"
            + $"　镜头 {rig.CenterX},{rig.CenterY}"
            + $"　震动位移 {rig.ShakeOffsetX},{rig.ShakeOffsetY}"
            + $"　推镜 {CameraFeel.EdgePushMarginCells}格/{CameraFeel.EdgePushPixelsPerSecond}"
            + $"　滚动 {CameraFeel.ScrollPixelsPerSecond}"
            + (rig.IsUnderCutsceneControl ? $"　演出接管：{rig.CutsceneReason}" : "") + "\n"
            + "F1 视角　F2 建造　F3 震一下　F4 震动开关　F9 演出　F10 打印数值\n"
            + "红线地图边界（角色走到这为止）　蓝线可建造区（线外那一圈是周边地形，本来就能走）"
            + "　黄线推镜触发带　脚手架没有碰撞与物理，那归玩法实现";
    }

    // ── 调试键 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 调试键。**事件驱动，不轮询** —— 理由见类注释。
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.F1:
                ToggleView();
                break;
            case Key.F2:
                ToggleBuildMode();
                break;
            case Key.F3:
                _camera.Rig.Shake();
                GD.Print("[脚手架] 震一下 幅度 ", CameraFeel.ShakeAmplitudeScreenPx,
                         " 屏幕像素｜时长 ", CameraFeel.ShakeSeconds, "s｜开关 ",
                         _camera.Rig.ShakeEnabled ? "开" : "关（应当一点都不动）");
                break;
            case Key.F4:
                _camera.Rig.ShakeEnabled = !_camera.Rig.ShakeEnabled;
                GD.Print("[脚手架] 震动开关 → ", _camera.Rig.ShakeEnabled ? "开" : "关");
                break;
            case Key.F9:
                StartCutscene();
                break;
            case Key.F10:
                PrintFeel();
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// 切视角。**换的是相机而不是相机里的分支** —— 视角是构造参数，切换等于换场景。
    /// </summary>
    private void ToggleView()
    {
        _view = _view == CameraView.TopDown ? CameraView.SideView : CameraView.TopDown;
        var buildMode = _camera.BuildMode;
        var shakeOn = _camera.Rig.ShakeEnabled;

        _camera.QueueFree();
        _camera = new GameCamera(_view) { FollowTarget = _actor, Router = _router };
        AddChild(_camera);
        _camera.BuildMode = buildMode;
        _camera.Rig.ShakeEnabled = shakeOn;
        ApplyBounds();
        _camera.Rig.SnapTo((int)_actor.Position.X, (int)_actor.Position.Y);
        _camera.Apply();

        ApplyActorLook();
        GD.Print("[脚手架] 视角 → ", _view, "（缩放 x", _camera.Rig.Zoom,
                 "，视野 ", _camera.Rig.VisibleWidth, "x", _camera.Rig.VisibleHeight, "）");
    }

    private void ToggleBuildMode()
    {
        _camera.BuildMode = !_camera.BuildMode;
        GD.Print("[脚手架] 建造模式 → ", _camera.BuildMode ? "开（方向键滚镜头）" : "关（方向键走人）");
    }

    private void StartCutscene()
    {
        if (_cutscene is not null)
        {
            GD.Print("[脚手架] 演出还没结束，不重复接管 —— 重复接管会抛，那是有意的");
            return;
        }

        _cutscene = _camera.Rig.TakeOver("脚手架：演出镜头摇到可建造区左上角");
        _cutscene.MoveTo(0, 0);
        _cutsceneLeft = 2.0;
        GD.Print("[脚手架] 演出接管相机 2 秒 —— 期间跟随空转，归还后直接对准角色");
    }

    private void PrintFeel()
    {
        var rig = _camera.Rig;
        GD.Print("[脚手架] 当前数值 死区 ", CameraFeel.DeadzoneHalfWidthScreenPx, "x",
                 CameraFeel.DeadzoneHalfHeightScreenPx, " 屏幕像素（世界 ",
                 rig.DeadzoneHalfWidth, "x", rig.DeadzoneHalfHeight, "）｜震动 ",
                 CameraFeel.ShakeAmplitudeScreenPx, "px ", CameraFeel.ShakeSeconds, "s ",
                 CameraFeel.ShakeHertz, "Hz｜推镜 ", CameraFeel.EdgePushMarginCells, " 格 ",
                 CameraFeel.EdgePushPixelsPerSecond, "px/s｜滚动 ",
                 CameraFeel.ScrollPixelsPerSecond, "px/s");
        GD.Print("[脚手架] 觉得哪个数不对就说，改 rules/Ui/CameraFeel.cs 即可（`UI-12`）");
    }

    // ── 小工具 ──────────────────────────────────────────────────────────

    /// <summary>地图边界比可建造区大一圈，所以钳制与推镜是两件能分开看的事。</summary>
    private void ApplyBounds()
    {
        _camera.Rig.SetWorldBounds(-MarginPx, -MarginPx,
            BuildableWidthPx + (MarginPx * 2), BuildableHeightPx + (MarginPx * 2));
        _camera.UseBuildableArea(_config.BuildableWidthCells, _config.BuildableHeightCells);
    }

    private void ApplyActorLook()
    {
        if (_view == CameraView.SideView)
        {
            _actor.Texture = _idleSheet;
            _actor.RegionRect = new Rect2(0, 0, SpriteFrame, SpriteFrame);
            return;
        }

        _actor.Texture = LoadTexture("res://assets/placeholder/chars/hero.png");
        _actor.RegionRect = new Rect2(0, 0, UiMetrics.IconLarge, UiMetrics.IconLarge);
    }

    /// <summary>载素材。**缺了就抛** —— 静默用空纹理会让脚手架看起来「什么都没画出来」。</summary>
    private static Texture2D LoadTexture(string path) =>
        ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path)
            : throw new FileNotFoundException($"脚手架缺素材：{path}（登记表在 tools/asset-registry.json）");
}
