using Godot;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.World;

/// <summary>
/// 启动时把 `UI-5` 的相机行为真跑一遍并打进日志（脚手架）。
/// </summary>
/// <remarks>
/// **为什么要有它**：规则层测试证明得了死区、钳制、震动可关与接管归还这些纯几何，证明不了
/// 「<c>Camera2D</c> 的 <c>Zoom</c> 真是那个整数」「位置平滑没被谁打开」「内置 <c>Limit*</c>
/// 没有第二份钳制在拉扯」，更证明不了**分数像素位移到底会不会破坏像素对齐** —— 那几件事要有
/// 引擎在场。引擎内测试底座还不存在（`ENG-6`），所以照 `UI-3`／`UI-7` 的办法来：跑一次、把判据
/// 打进 `--log-file`，由 `tools/check_camera.py` 读回判定。
///
/// **每条判据在两种视角下各跑一遍。** 这是「两种视角共用同一份实现」的行为执行体：某个行为只在
/// 一种视角下成立时，另一种视角会缺条，而守卫核的正是「两边的判据名字集合完全相同」。
///
/// 最后一段是**实测**而不是判据：把相机放到分数像素上截图，量最小同色跑长，看像素块有没有被切开。
/// 结论写进 `UI-5` 的实现笔记，不凭记忆写。反证形状刻意保留在这里 —— 一个从不制造失败的量具
/// 无法证明自己量得出失败。
///
/// **`UI-10` 的端到端测试会替换它。** 与 <c>Main.ProbeUiSkeleton</c>、<c>InputProbe</c> 同一性质。
/// </remarks>
public partial class CameraProbe : Node2D
{
    /// <summary>棋盘格的两种颜色。刻意取界面里不会出现的纯色，免得把 UI 像素数成条纹。</summary>
    private static readonly Color StripeA = Color.Color8(255, 0, 255);
    private static readonly Color StripeB = Color.Color8(0, 255, 0);

    /// <summary>像素对齐实测那几条判据的标签。**不是视角**，所以不参与两视角的名字集合比对。</summary>
    private const string PixelTag = "像素对齐";

    private const string IntegerTag = "整数位置";
    private const string FractionalTag = "分数位置";
    private const string BrokenZoomTag = "非整数总缩放";

    /// <summary>
    /// 反证用的相机缩放。1.25 × 视口缩放 2 = 2.5，**总缩放不是整数**，这才是真会切开像素块的形状。
    /// </summary>
    /// <remarks>
    /// 不能取 1.5：1.5 × 2 = 3 仍是整数，量出来照样均匀，反证会假过。
    /// </remarks>
    private const float BrokenZoom = 1.25f;

    private const int PatternSize = 512;

    private readonly GameConfig _config;
    private readonly List<Action> _steps = [];
    private int _index;
    private int _checks;
    private int _passed;

    private GameCamera? _camera;
    private Sprite2D? _pattern;
    private float _fractionalOffset;
    private int _expectedRun = -1;

    /// <summary>每次截图量到的最小同色跑长，按标签存。</summary>
    private readonly Dictionary<string, int> _runs = [];

    /// <summary>整数位置那次的截图，用来与分数位置那次逐像素比对。</summary>
    private Image? _baseline;

    public CameraProbe(GameConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// 自检跑完了。**验收脚手架要等这个信号才建自己的相机。**
    /// </summary>
    /// <remarks>
    /// 不等的话两台相机会抢 <c>current</c>：本探针为了两种视角各测一遍，会反复建与释放相机，
    /// 而 <see cref="CameraHarness"/> 要的是一台长期活着的。谁 current 由建立顺序决定，
    /// 那种依赖时序的正确性正是「实机上偶尔不对」的来源。
    /// </remarks>
    public event Action? Finished;

    /// <summary>
    /// 这次运行有真的渲染设备吗。`--headless` 下没有，截图取回来是空的。
    /// </summary>
    /// <remarks>
    /// 判据取 <see cref="DisplayServer.GetName"/>：headless 时它就是 <c>"headless"</c>，
    /// 而那时渲染走 dummy 后端，<c>GetViewport().GetTexture()</c> 返回空。
    /// 2026-08-31 实测过硬取的后果：一串 <c>Parameter "t" is null</c> 加 NullReference，
    /// 而 `verify.py` 的跑产物那步正是 headless，于是门禁判失败。
    /// </remarks>
    private static bool HasRenderingDevice => DisplayServer.GetName() != "headless";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;    // 手环面板可能让世界暂停，自检不该跟着停
        DescribeSettings();

        foreach (var view in Enum.GetValues<CameraView>())
        {
            CheckGeometry(view);
        }

        BuildEngineSteps();
    }

    public override void _Process(double delta)
    {
        if (_index >= _steps.Count)
        {
            Report();
            SetProcess(false);
            Cleanup();
            Finished?.Invoke();
            return;
        }

        _steps[_index++]();
    }

    /// <summary>
    /// 收尾：把自己建的相机释放掉，**场景里不留副产物**。
    /// </summary>
    /// <remarks>
    /// 这条是踩坑记录第 35 条那个形状：注入型自证的还原必须覆盖被测系统的副产物。不释放的话
    /// 探针最后那台相机会一直留在场景里，而 `UI-8` 与 `UI-9` 要往同一个场景加 HUD 与世界空间
    /// UI —— 那时「画面糊」或「读条跟错了相机」会先去怀疑那两条。
    /// </remarks>
    private void Cleanup()
    {
        _camera?.QueueFree();
        _camera = null;
        GD.Print("[相机] 探针收尾 已释放自己建的相机，场景里不留副产物");
    }

    // ── 规则层几何：两种视角各跑一遍同一批判据 ──────────────────────────

    /// <summary>
    /// 同一批判据，两种视角各跑一遍。**判据名字必须逐字相同**，守卫按名字集合比对两边。
    /// </summary>
    private void CheckGeometry(CameraView view)
    {
        var rig = new CameraRig(view);
        rig.SetLogicalViewport(UiMetrics.BaseWidth, UiMetrics.BaseHeight);
        GD.Print("[相机] 视角 ", view, " ｜ 缩放 ", rig.Zoom,
                 " ｜ 视野 ", rig.VisibleWidth, "x", rig.VisibleHeight,
                 " ｜ 死区 ", rig.DeadzoneHalfWidth, "x", rig.DeadzoneHalfHeight,
                 " ｜ 实现 ", rig.GetType().FullName);

        // ① 缩放是正整数，且侧视那个数取自正典
        var wantZoom = view == CameraView.SideView ? UiMetrics.SideViewZoom : 1;
        Check(view, "缩放是正整数且与视角登记一致", rig.Zoom == wantZoom, $"实际 {rig.Zoom}，期望 {wantZoom}");
        Check(view, "有效视野是逻辑分辨率除以缩放",
            rig.VisibleWidth == UiMetrics.BaseWidth / rig.Zoom
            && rig.VisibleHeight == UiMetrics.BaseHeight / rig.Zoom,
            $"{rig.VisibleWidth}x{rig.VisibleHeight}");

        // ② 死区：内不动，出了才跟；两种视角取景一致
        rig.SnapTo(1000, 1000);
        var half = rig.DeadzoneHalfWidth;
        var stillInside = !rig.Follow(1000 + half, 1000) && rig.CenterX == 1000;
        var movedOutside = rig.Follow(1000 + half + 1, 1000) && rig.CenterX == 1001;
        Check(view, "死区内移动镜头不动、出了死区才跟", stillInside && movedOutside,
            $"贴边不动 {stillInside}，出边跟随 {movedOutside}");
        Check(view, "死区换算回屏幕像素两种视角一致",
            rig.DeadzoneHalfWidth * rig.Zoom == CameraFeel.DeadzoneHalfWidthScreenPx,
            $"{rig.DeadzoneHalfWidth}×{rig.Zoom} = {rig.DeadzoneHalfWidth * rig.Zoom}");

        // ③ 钳制：视口不越界；视野比地图宽时居中
        rig.SetWorldBounds(0, 0, 2000, 1200);
        rig.SnapTo(-9999, -9999);
        var atMin = rig.CenterX - (rig.VisibleWidth / 2) >= 0 && rig.CenterY - (rig.VisibleHeight / 2) >= 0;
        rig.SnapTo(9999, 9999);
        var atMax = rig.CenterX + (rig.VisibleWidth / 2) <= 2000
                    && rig.CenterY + (rig.VisibleHeight / 2) <= 1200;
        Check(view, "相机被钳在地图内、视口不越出边界", atMin && atMax,
            $"左上 {atMin}，右下 {atMax}，中心 {rig.CenterX},{rig.CenterY}");

        var narrow = new CameraRig(view);
        narrow.SetLogicalViewport(UiMetrics.BaseWidth, UiMetrics.BaseHeight);
        narrow.SetWorldBounds(0, 0, narrow.VisibleWidth / 2, narrow.VisibleHeight / 2);
        narrow.SnapTo(9999, 9999);
        Check(view, "视野比地图宽时居中而不是顶到一边",
            !narrow.ClampsHorizontally && narrow.CenterX == narrow.VisibleWidth / 4,
            $"钳制 {narrow.ClampsHorizontally}，中心 {narrow.CenterX}，" +
            $"期望 {narrow.VisibleWidth / 4}");

        // ④ 震动：整数位移、幅度不超登记值、关掉恒零
        rig.Shake();
        var maxScreen = 0;
        var stepped = 0;
        while (rig.IsShaking && stepped < 64)
        {
            maxScreen = Math.Max(maxScreen,
                Math.Max(Math.Abs(rig.ShakeOffsetX), Math.Abs(rig.ShakeOffsetY)) * rig.Zoom);
            rig.Advance(CameraFeel.ShakeSeconds / 8);
            stepped++;
        }

        Check(view, "震动幅度不超过登记值且到时自停",
            maxScreen == CameraFeel.ShakeAmplitudeScreenPx && !rig.IsShaking,
            $"最大 {maxScreen} 屏幕像素，登记 {CameraFeel.ShakeAmplitudeScreenPx}，推进 {stepped} 步");

        rig.ShakeEnabled = false;
        rig.Shake();
        var zeroed = true;
        for (var i = 0; i < 8; i++)
        {
            zeroed &= rig.ShakeOffsetX == 0 && rig.ShakeOffsetY == 0;
            rig.Advance(CameraFeel.ShakeSeconds / 8);
        }

        Check(view, "关掉震动后重击不产生任何位移", zeroed && !rig.IsShaking, $"恒零 {zeroed}");
        rig.ShakeEnabled = true;

        // ⑤ 演出接管：接管期间跟随空转，归还后行为与接管前一致
        rig.SnapTo(1000, 600);
        var zoomBefore = rig.Zoom;
        var halfBefore = rig.DeadzoneHalfWidth;
        var lease = rig.TakeOver($"自检：{view} 的演出镜头");
        lease.OverrideZoom(zoomBefore + 2);
        rig.ShakeEnabled = false;
        lease.MoveTo(1500, 800);
        var idleWhileLeased = !rig.Follow(1000, 600) && rig.CenterX == 1500;
        lease.Release();
        var restored = rig.Zoom == zoomBefore && rig.ShakeEnabled
                       && rig.DeadzoneHalfWidth == halfBefore;
        rig.Follow(1200, 700);
        var resnapped = rig.CenterX == 1200 && rig.CenterY == 700;
        Check(view, "演出接管期间跟随空转、归还后行为与接管前一致",
            idleWhileLeased && restored && resnapped,
            $"接管期空转 {idleWhileLeased}，参数还原 {restored}，归还后对准目标 {resnapped}");

        var refused = false;
        using (rig.TakeOver($"自检：{view} 的重复接管"))
        {
            try
            {
                rig.TakeOver("第二段演出");
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }
        }

        Check(view, "重复接管被拒而不是静默顶掉前一段", refused && !rig.IsUnderCutsceneControl,
            $"拒绝 {refused}，归还后无人接管 {!rig.IsUnderCutsceneControl}");

        // ⑥ 建造：可建造区尺寸来自配置，滚动与推镜可用且不改缩放
        rig.SetWorldBounds(-640, -480, 2560, 1920);
        rig.SnapTo(0, 0);
        rig.SetBuildableArea(0, 0,
            _config.BuildableWidthCells * UiMetrics.BaseUnit,
            _config.BuildableHeightCells * UiMetrics.BaseUnit);
        GD.Print("[相机] 装进相机的可建造区 ", rig.BuildableWidth, "x", rig.BuildableHeight, "px");
        var scrolled = rig.Scroll(CameraFeel.ScrollPixelsPerSecond * 0.5, 0);
        var zoomKept = rig.Zoom == wantZoom;
        Check(view, "建造时相机可滚动且不改缩放", scrolled && zoomKept,
            $"滚动 {scrolled}，缩放仍为 {rig.Zoom}");

        rig.SnapTo(320, 240);
        var pushed = rig.PushFromEdge(CameraFeel.EdgePushMarginPixels - 1, 240, 1.0);
        var pushedLeft = rig.CenterX < 320;
        rig.SnapTo(320, 240);
        var quiet = !rig.PushFromEdge(
            _config.BuildableWidthCells * UiMetrics.BaseUnit / 2,
            _config.BuildableHeightCells * UiMetrics.BaseUnit / 2, 1.0);
        Check(view, "角色靠近可建造区边缘时推镜、在中间时不推",
            pushed && pushedLeft && quiet,
            $"推了 {pushed}，方向朝边 {pushedLeft}，中间安静 {quiet}");
    }

    // ── 引擎层：节点属性与像素对齐实测 ──────────────────────────────────

    private void BuildEngineSteps()
    {
        foreach (var view in Enum.GetValues<CameraView>())
        {
            var captured = view;
            Step(() => MountCamera(captured));
            Step(() => CheckNode(captured));
        }

        // 像素对齐实测**需要真的渲染设备**。`--headless` 下 `GetViewport().GetTexture()` 返回
        // 空（dummy 渲染后端没有纹理），硬取会抛 NullReference 并往日志里灌一串 ERROR ——
        // 而 verify.py 的跑产物那步正是用 headless 跑的，那些 ERROR 会让门禁判失败。
        // 所以这里显式跳过并打出理由：跳过要看得见，不能静默少测。
        if (!HasRenderingDevice)
        {
            Step(() => GD.Print("[相机] 量 跳过 ｜ 显示后端 ", DisplayServer.GetName(),
                                " 没有渲染设备，像素对齐实测要真截图 —— "
                                + "这一项由 tools/check_camera.py 在带窗口的运行里判"));
            return;
        }

        // 侧视：缩放 2 是本作真正会遇到的最坏情况。三次截图各回答一个问题 ——
        // 基线量得对吗、相机落在分数像素上会怎样、总缩放不是整数会怎样。
        Step(() => MountCamera(CameraView.SideView));
        Step(FreezeCamera);
        Step(BuildPattern);
        Step(Idle);
        Step(Idle);
        Step(() => Grab(IntegerTag));
        Step(ShiftToFractional);
        Step(Idle);
        Step(Idle);
        Step(() => Grab(FractionalTag));
        Step(BreakZoom);
        Step(Idle);
        Step(Idle);
        Step(() => Grab(BrokenZoomTag));
        Step(ReportMeasurement);
    }

    private void MountCamera(CameraView view)
    {
        _camera?.QueueFree();
        _camera = new GameCamera(view);
        AddChild(_camera);
        _camera.Rig.SnapTo(0, 0);
        _camera.Apply();
    }

    /// <summary>核那几件只有引擎在场才证明得了的事。</summary>
    private void CheckNode(CameraView view)
    {
        var camera = _camera!;
        Check(view, "节点缩放与规则层算出的整数缩放一致",
            Mathf.IsEqualApprox(camera.Zoom.X, camera.Rig.Zoom)
            && Mathf.IsEqualApprox(camera.Zoom.Y, camera.Rig.Zoom),
            $"节点 {camera.Zoom.X}x{camera.Zoom.Y}，规则层 {camera.Rig.Zoom}");

        Check(view, "位置平滑关闭_否则镜头会落在分数像素上",
            !camera.PositionSmoothingEnabled && !camera.RotationSmoothingEnabled,
            $"位置平滑 {camera.PositionSmoothingEnabled}，旋转平滑 {camera.RotationSmoothingEnabled}");

        // 期望值取**引擎自己的默认相机**，不写死我记得的那几个数：Godot 各版本改过这些默认值，
        // 写死会让这条检查在某次升级后静默变成「永远通过」。
        using var stock = new Camera2D();
        Check(view, "内置Limit未启用_钳制只有规则层那一份",
            camera.LimitLeft == stock.LimitLeft && camera.LimitTop == stock.LimitTop
            && camera.LimitRight == stock.LimitRight && camera.LimitBottom == stock.LimitBottom,
            $"本相机 {camera.LimitLeft},{camera.LimitTop},{camera.LimitRight},{camera.LimitBottom}" +
            $"｜引擎默认 {stock.LimitLeft},{stock.LimitTop},{stock.LimitRight},{stock.LimitBottom}");

        camera.Rig.SnapTo(37, 91);
        camera.Rig.Shake();
        camera.Apply();
        var pos = camera.Position + camera.Offset;
        Check(view, "相机位置是整数世界像素",
            Mathf.IsEqualApprox(pos.X, Mathf.Round(pos.X))
            && Mathf.IsEqualApprox(pos.Y, Mathf.Round(pos.Y)),
            $"位置 {camera.Position.X},{camera.Position.Y} 加震动 {camera.Offset.X},{camera.Offset.Y}");

        Check(view, "本节点没有覆盖纹理过滤",
            camera.TextureFilter == TextureFilterEnum.ParentNode,
            $"实际 {camera.TextureFilter}");

        Check(view, "这台相机是当前相机_世界空间UI才跟得上",
            camera.IsCurrent(), $"IsCurrent {camera.IsCurrent()}");
    }

    private void DescribeSettings()
    {
        GD.Print("[相机] 可建造区 ", _config.BuildableWidthCells, "x", _config.BuildableHeightCells,
                 " 格（", _config.BuildableWidthCells * UiMetrics.BaseUnit, "x",
                 _config.BuildableHeightCells * UiMetrics.BaseUnit, "px）来自配置 ",
                 GameConfig.ContentPath);
        GD.Print("[相机] 手感初值 死区 ", CameraFeel.DeadzoneHalfWidthScreenPx, "x",
                 CameraFeel.DeadzoneHalfHeightScreenPx, " 屏幕像素｜震动 ",
                 CameraFeel.ShakeAmplitudeScreenPx, " 屏幕像素 ", CameraFeel.ShakeSeconds, "s ",
                 CameraFeel.ShakeHertz, "Hz｜推镜 ", CameraFeel.EdgePushMarginCells, " 格 ",
                 CameraFeel.EdgePushPixelsPerSecond, "px/s｜滚动 ",
                 CameraFeel.ScrollPixelsPerSecond, "px/s（UI-12 待实机收敛）");

        // 2D 像素吸附：本作**不靠**它，相机自己保证整数。打出来是为了让「靠不靠它」有据可查 ——
        // 若哪天有人打开它，分数位移的反证会失效，而那时守卫必须能看出来。
        foreach (var key in (string[])["rendering/2d/snap/snap_2d_transforms_to_pixel",
                                      "rendering/2d/snap/snap_2d_vertices_to_pixel"])
        {
            GD.Print("[相机] 设置 ", key, " = ",
                     ProjectSettings.HasSetting(key) ? ProjectSettings.GetSetting(key) : "（无此项）");
        }
    }

    // ── 像素对齐实测 ────────────────────────────────────────────────────

    /// <summary>铺一张 1px 棋盘格。**必须走纹理采样** —— 要量的正是最近邻采样在分数偏移下的行为。</summary>
    private void BuildPattern()
    {
        var image = Image.CreateEmpty(PatternSize, PatternSize, false, Image.Format.Rgba8);
        for (var y = 0; y < PatternSize; y++)
        {
            for (var x = 0; x < PatternSize; x++)
            {
                image.SetPixel(x, y, (x + y) % 2 == 0 ? StripeA : StripeB);
            }
        }

        _pattern = new Sprite2D
        {
            Name = "PixelAlignmentPattern",
            Texture = ImageTexture.CreateFromImage(image),
            Centered = false,
            Position = new Vector2(-PatternSize / 2, -PatternSize / 2),
            ZIndex = -100,
        };
        AddChild(_pattern);
    }

    private void Idle()
    {
    }

    /// <summary>
    /// 量之前先停掉相机自己的每帧同步。
    /// </summary>
    /// <remarks>
    /// 这一步是**踩出来的**：第一版没有它，反证测到「分数位置也对齐」，看起来像引擎自己会吸附。
    /// 实际是 <see cref="GameCamera._Process"/> 在同一帧的后面把位置抄回整数了 —— 节点树按父先
    /// 子后的顺序处理，本探针是父、相机是子。也就是说那次测的根本不是分数位置。
    /// 所以两次截图都在冻结状态下取，条件才一致；判据里另核「分数那次的位置真的是分数」。
    /// </remarks>
    private void FreezeCamera() => _camera!.SetProcess(false);

    private void ShiftToFractional()
    {
        var camera = _camera!;
        var scale = GetViewport().GetFinalTransform().Scale.X;

        // 挑一个**在本次缩放下必然不是整数物理像素**的偏移：世界像素 × 相机缩放 × 视口缩放 = 0.5。
        // 写死 0.5 世界像素是不行的 —— 缩放是偶数时 0.5 世界像素恰好等于整数物理像素，反证会假过。
        _fractionalOffset = 1f / (camera.Rig.Zoom * scale * 2f);
        camera.Position += new Vector2(_fractionalOffset, 0f);
        GD.Print("[相机] 反证 相机 X 偏移 ", _fractionalOffset.ToString("0.0000"),
                 " 世界像素（× 相机缩放 ", camera.Rig.Zoom, " × 视口缩放 ", scale.ToString("0.###"),
                 " = ", (_fractionalOffset * camera.Rig.Zoom * scale).ToString("0.###"),
                 " 物理像素）");
    }

    /// <summary>把相机的总缩放弄成非整数。**这是反证** —— 量具要能量出失败才算量具。</summary>
    private void BreakZoom()
    {
        var camera = _camera!;
        camera.Position -= new Vector2(_fractionalOffset, 0f);   // 位置先还原成整数，只留缩放这一个变量
        camera.Zoom = new Vector2(BrokenZoom, BrokenZoom);
        GD.Print("[相机] 反证 相机缩放改为 ", BrokenZoom.ToString("0.###"),
                 "（× 视口缩放 ", GetViewport().GetFinalTransform().Scale.X.ToString("0.###"),
                 " = 总缩放非整数）");
    }

    private void Grab(string tag)
    {
        var image = GetViewport().GetTexture().GetImage();
        var scale = Mathf.RoundToInt(GetViewport().GetFinalTransform().Scale.X);
        var camera = _camera!;
        var (run, samples) = MinColorRun(image);
        _runs[tag] = run;
        if (tag == IntegerTag)
        {
            _expectedRun = scale * camera.Rig.Zoom;
            _baseline = image;
        }

        // 把截图那一刻的相机位置与节点缩放一起打出来。**这不是诊断而是判据的一部分**：不打的话
        // 「引擎不受分数位置影响」与「偏移根本没生效」在日志里长得一模一样（本轮第一版就栽在这里）。
        var pos = camera.Position;
        var diff = tag == IntegerTag ? "基线" : CompareToBaseline(image);
        GD.Print("[相机] 量 ", tag,
                 " ｜ 相机位置 ", pos.X.ToString("0.0000"), ",", pos.Y.ToString("0.0000"),
                 " ｜ 节点缩放 ", camera.Zoom.X.ToString("0.####"),
                 " ｜ 取回图 ", image.GetWidth(), "x", image.GetHeight(),
                 " ｜ 视口缩放 x", scale, " ｜ 期望跑长 ", _expectedRun,
                 " ｜ 最小同色跑长 ", run, " ｜ 统计段数 ", samples,
                 " ｜ 与基线 ", diff);
    }

    /// <summary>
    /// 与基线截图逐像素比一遍，报「一模一样」还是「整体平移了几个物理像素」。
    /// </summary>
    /// <remarks>
    /// 为什么需要这个而不只看最小跑长：最小跑长对**整体平移**是不敏感的（整数总缩放下平移多少都
    /// 不改变每个世界像素占几个物理像素），所以只看它分不清「分数位置被引擎无视了」和「分数位置
    /// 让画面挪了一格」。这两件事对结论的影响不同，得分开报。
    /// </remarks>
    private string CompareToBaseline(Image image)
    {
        if (_baseline is null || _baseline.GetSize() != image.GetSize())
        {
            return "尺寸不同，无法比";
        }

        var best = 0;
        var bestMatch = -1.0;
        for (var shift = -3; shift <= 3; shift++)
        {
            var match = MatchRatio(_baseline, image, shift);
            if (match > bestMatch)
            {
                bestMatch = match;
                best = shift;
            }
        }

        var exact = MatchRatio(_baseline, image, 0);
        return exact >= 0.9999
            ? "一模一样（分数位置没改变任何物理像素）"
            : $"最佳对齐平移 {best} 物理像素（吻合 {bestMatch:P2}，原位吻合 {exact:P2}）";
    }

    private static double MatchRatio(Image a, Image b, int shift)
    {
        var w = a.GetWidth();
        var h = a.GetHeight();
        var same = 0;
        var total = 0;
        for (var y = 0; y < h; y += 4)
        {
            for (var x = 8; x < w - 8; x += 2)
            {
                var pa = a.GetPixel(x, y);
                if (!IsStripe(pa))
                {
                    continue;
                }

                total++;
                if (pa.IsEqualApprox(b.GetPixel(x + shift, y)))
                {
                    same++;
                }
            }
        }

        return total == 0 ? 0.0 : (double)same / total;
    }

    /// <summary>
    /// 扫棋盘格区域，取最短的一段连续同色像素。
    /// </summary>
    /// <remarks>
    /// 判据不是「看着像块状」而是**最小连续同色跑长必须等于视口缩放乘相机缩放**：1px 的格子在
    /// ×N 下应当摊成 N 个物理像素。一旦相机落在分数位置，采样边界就会切在格子中间，出现 N−1 与
    /// N+1 交替的段，最小跑长立刻掉下来。
    ///
    /// 只统计**两侧都是格子色**的段：贴到界面元素或屏幕边缘的段可能被截断，算进来会假失败。
    /// </remarks>
    private static (int Run, int Samples) MinColorRun(Image image)
    {
        var w = image.GetWidth();
        var h = image.GetHeight();
        var best = int.MaxValue;
        var samples = 0;

        for (var y = 0; y < h; y += 2)
        {
            var x = 0;
            while (x < w)
            {
                if (!IsStripe(image.GetPixel(x, y)))
                {
                    x++;
                    continue;
                }

                var start = x;
                var color = image.GetPixel(x, y);
                while (x < w && image.GetPixel(x, y).IsEqualApprox(color))
                {
                    x++;
                }

                var boundedLeft = start > 0 && IsStripe(image.GetPixel(start - 1, y));
                var boundedRight = x < w && IsStripe(image.GetPixel(x, y));
                if (boundedLeft && boundedRight)
                {
                    best = Math.Min(best, x - start);
                    samples++;
                }
            }
        }

        return (samples == 0 ? -1 : best, samples);
    }

    private static bool IsStripe(Color color) =>
        color.IsEqualApprox(StripeA) || color.IsEqualApprox(StripeB);

    private void ReportMeasurement()
    {
        _pattern?.QueueFree();
        _pattern = null;
        _baseline = null;

        // **反证改过的状态必须还原，而且还原本身要有判据。** 不还原的话这台相机会带着 1.25 的
        // 非整数缩放、且每帧同步是关着的，一直留在场景里 —— 而 `UI-8` 与 `UI-9` 要往同一个场景
        // 加 HUD 与世界空间 UI，那时才发现「画面糊」会先怀疑那两条。同一个道理在
        // 设计仓踩坑记录第 35 条写过：自证的还原必须覆盖被测系统的副产物。
        var camera = _camera!;
        camera.SetProcess(true);
        camera.Apply();

        var integer = _runs.GetValueOrDefault(IntegerTag, -1);
        var fractional = _runs.GetValueOrDefault(FractionalTag, -1);
        var broken = _runs.GetValueOrDefault(BrokenZoomTag, -1);

        Check(PixelTag, "整数位置下像素块恰好等于视口缩放乘相机缩放",
            integer == _expectedRun,
            $"最小跑长 {integer}，期望 {_expectedRun}");

        // **这一条是实测纠正过的结论，不是当初以为的那个。** 原以为分数位置会把像素块切成宽窄
        // 不一的条；实测最小跑长一动不动。原因是可以算出来的：总缩放是整数 S 时，每个世界像素的
        // 屏幕跨度 [kS+d, (k+1)S+d) 恰好包住 S 个像素中心，与偏移 d 无关。所以分数相机位置在
        // 整数总缩放下**不改变任何物理像素**，取整的理由不是清晰度而是运动可预期（见实现笔记）。
        Check(PixelTag, "分数位置不改变像素块尺寸_整数总缩放下平移不影响采样",
            fractional == _expectedRun,
            $"最小跑长 {fractional}，与整数位置的 {integer} 相同（偏移 " +
            $"{_fractionalOffset.ToString("0.0000")} 世界像素）");

        // 反证：真会切开像素块的是**非整数总缩放**，而那正是代码里挡住的东西
        // （CameraRig.Zoom 只能是正整数，演出接管改缩放也只收正整数）。
        Check(PixelTag, "非整数总缩放会切开像素块_所以缩放只收正整数",
            broken > 0 && broken < _expectedRun,
            $"最小跑长 {broken}，期望小于 {_expectedRun}（相机缩放 " +
            $"{BrokenZoom.ToString("0.###")}）");

        Check(PixelTag, "反证改过的状态已还原_没把非整数缩放留给场景",
            Mathf.IsEqualApprox(camera.Zoom.X, camera.Rig.Zoom)
            && Mathf.IsEqualApprox(camera.Zoom.Y, camera.Rig.Zoom)
            && camera.IsProcessing(),
            $"节点缩放 {camera.Zoom.X}（规则层 {camera.Rig.Zoom}）｜每帧同步 " +
            $"{camera.IsProcessing()}");
    }

    private void Report() =>
        GD.Print("[相机] 自检 ", _passed, "/", _checks, " 条通过");

    private void Step(Action step) => _steps.Add(step);

    private void Check(CameraView view, string what, bool ok, string detail) =>
        Check(view.ToString(), what, ok, detail);

    /// <summary>
    /// 打一条判据。
    /// </summary>
    /// <remarks>
    /// <paramref name="tag"/> 取视角名时，这条判据**必须在两种视角下各出现一次** ——
    /// `check_camera.py` 按名字集合比对两边，缺一条就说明某个行为只在一种视角下成立。
    /// 像素对齐实测那两条不属于任何视角，所以走 <see cref="PixelTag"/>，不参与那个比对。
    /// </remarks>
    private void Check(string tag, string what, bool ok, string detail)
    {
        _checks++;
        if (ok)
        {
            _passed++;
        }

        GD.Print("[相机] 判据 ", ok ? "PASS" : "FAIL", " ｜ ", tag, " ｜ ", what, " ｜ ", detail);
    }
}
