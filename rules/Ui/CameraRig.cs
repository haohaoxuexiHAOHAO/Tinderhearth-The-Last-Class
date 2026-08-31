namespace Tinderhearth.Rules.Ui;

/// <summary>视角。**只有这两种，且共用同一份相机实现** —— 正典写明「无例外」。</summary>
public enum CameraView
{
    /// <summary>基地与城区。1 倍缩放。</summary>
    TopDown,

    /// <summary>战斗与出征关卡。2 倍整数缩放，有效视野是逻辑分辨率的一半。</summary>
    SideView,
}

/// <summary>
/// 一台相机的全部行为（`UI-5`）：跟随、钳制、震动、演出接管、建造滚动与边缘推镜。
/// </summary>
/// <remarks>
/// **两种视角共用这一个类，视角只是构造参数。** 这不是省事，是正典点名的立项理由：
/// 「行为不定下来，两种视角会各写一套」。所以视角在这里只影响一件事 —— <see cref="Zoom"/>，
/// 以及由它换算出来的可视尺寸与死区。其余判定逐字相同。执行体有两个：
/// `tools/check_camera.py` 静态核「引擎层只有一个 <c>Camera2D</c> 派生类型且它是 sealed」，
/// 并要求启动自检的每条判据在**两种视角下各出现一次**（某个行为只在一种视角下成立就会缺条）。
///
/// **为什么整台状态机在规则层。** 相机的失效方式都不报错：死区写成 0 只表现为「镜头有点抖」，
/// 钳制少一边只表现为「地图边上偶尔露白」，演出结束忘了归还只表现为「后面镜头不动了」。
/// 这些都是纯几何，放这里就能用不启引擎的单元测试盯住。引擎层只剩一件事：把
/// <see cref="CenterX"/> 与 <see cref="ShakeOffsetX"/> 抄进 <c>Camera2D</c>。
///
/// **一切对外暴露的位置量都是整数世界像素。** 速度驱动的移动（滚动、推镜）把不足一像素的部分
/// 留在内部余量里累加，不四舍五入 —— 这样「输出恒为整数」是结构保证而不是某一行取整的结果。
/// 理由是实测的：相机落在半像素上时最近邻采样会把像素块切成宽窄不一的条（见 `UI-5` 实现笔记），
/// 而那看起来只是「画面有点脏」，不报错。
///
/// **不做位置平滑。** 平滑要引入一个时间常数，那又是一个只能实机收敛的数；而硬死区的行为可预期，
/// 正是正典对相机的要求。想要软化的场合（演出）由演出脚本自己驱动镜头。
/// </remarks>
public sealed class CameraRig
{
    private int _logicalWidth = UiMetrics.BaseWidth;
    private int _logicalHeight = UiMetrics.BaseHeight;

    private int _centerX;
    private int _centerY;

    // 速度驱动的移动留在这里累加，攒够一个整像素才动镜头。
    private double _remainderX;
    private double _remainderY;

    private Bounds? _world;
    private Bounds? _buildable;

    private int _zoomOverride;
    private CameraCutscene? _cutscene;
    private bool _resnapOnNextFollow;

    private double _shakeElapsed = double.PositiveInfinity;
    private double _shakeSeconds;
    private int _shakeAmplitudeScreenPx;

    public CameraRig(CameraView view)
    {
        View = view;
    }

    /// <summary>这台相机服务哪种视角。构造后不变 —— 切视角是换场景，不是改相机。</summary>
    public CameraView View { get; }

    /// <summary>
    /// 整数缩放倍数。俯视 1 倍，侧视取 <see cref="UiMetrics.SideViewZoom"/>。
    /// </summary>
    /// <remarks>
    /// 侧视那个 2 **不在这里重定义**：它是正典的像素基准，已经在 <see cref="UiMetrics"/> 里，
    /// 且那边有测试钉住「有效视野 = 320×180」。演出期间可以临时覆盖，但只能覆盖成正整数。
    /// </remarks>
    public int Zoom => _zoomOverride > 0
        ? _zoomOverride
        : View == CameraView.SideView ? UiMetrics.SideViewZoom : 1;

    /// <summary>
    /// 告诉相机当前的逻辑视口尺寸。**必须每帧传，不能缓存成常量。**
    /// </summary>
    /// <remarks>
    /// `aspect="expand"` 下逻辑宽度是**下限不是定值**：高度锁在 360，宽度按窗口宽高比撑开
    /// （`UI-3` 实测 3840×2130 的窗口得到逻辑 649×360）。所以按 640 算钳制范围会错 ——
    /// 宽窗口上相机会停得太早，地图边缘露白。
    /// </remarks>
    public void SetLogicalViewport(int logicalWidth, int logicalHeight)
    {
        if (logicalWidth <= 0 || logicalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(logicalWidth), $"逻辑视口必须为正：{logicalWidth}×{logicalHeight}");
        }

        _logicalWidth = logicalWidth;
        _logicalHeight = logicalHeight;
        Reclamp();
    }

    /// <summary>能看到的世界宽度，世界像素。**向上取整**，理由见 <see cref="VisibleHeight"/>。</summary>
    public int VisibleWidth => CeilDiv(_logicalWidth, Zoom);

    /// <summary>
    /// 能看到的世界高度，世界像素。
    /// </summary>
    /// <remarks>
    /// 取上整而不是截断：逻辑宽度可能是奇数（`expand` 撑出来的 649），截断会让相机以为自己看得
    /// 比实际少半像素，于是钳制放得太松、边缘露出半像素的白缝。往大取只会让钳制更保守。
    /// </remarks>
    public int VisibleHeight => CeilDiv(_logicalHeight, Zoom);

    /// <summary>跟随死区的半宽，世界像素。屏幕像素除以缩放 —— 两种视角取景一致。</summary>
    public int DeadzoneHalfWidth => CameraFeel.DeadzoneHalfWidthScreenPx / Zoom;

    /// <summary>跟随死区的半高，世界像素。</summary>
    public int DeadzoneHalfHeight => CameraFeel.DeadzoneHalfHeightScreenPx / Zoom;

    /// <summary>钳制用的地图边界。没设过就不钳制（测试与演出场景可以不设）。</summary>
    public bool HasWorldBounds => _world is not null;

    /// <summary>
    /// 装进相机的可建造区宽度，世界像素；没设过是 0。
    /// </summary>
    /// <remarks>
    /// 暴露出来是给守卫用的：`check_camera.py` 拿它与 Python 自己解出来的
    /// `data/config/game.json` 比对，于是「有人在代码里写死 40×30」躲不过去 —— 打印配置里的值
    /// 只能证明配置读到了，证明不了装进相机的是那个值。
    /// </remarks>
    public int BuildableWidth => _buildable?.Width ?? 0;

    /// <inheritdoc cref="BuildableWidth"/>
    public int BuildableHeight => _buildable?.Height ?? 0;

    /// <summary>横向真的会被钳制吗。视野比地图还宽时不钳制而是居中，见 <see cref="Reclamp"/>。</summary>
    public bool ClampsHorizontally => _world is { } w && VisibleWidth < w.Width;

    /// <summary>纵向真的会被钳制吗。</summary>
    public bool ClampsVertically => _world is { } w && VisibleHeight < w.Height;

    /// <summary>相机中心的横坐标，整数世界像素，**已钳制**。</summary>
    public int CenterX => _centerX;

    /// <summary>相机中心的纵坐标，整数世界像素，**已钳制**。</summary>
    public int CenterY => _centerY;

    /// <summary>震动的横向位移，整数世界像素。关掉震动时恒为 0。</summary>
    public int ShakeOffsetX => ShakeAxis(phase: 0);

    /// <summary>震动的纵向位移，整数世界像素。关掉震动时恒为 0。</summary>
    public int ShakeOffsetY => ShakeAxis(phase: 1);

    /// <summary>
    /// 屏幕震动的总开关。**设置里那一项直接落在这里。**
    /// </summary>
    /// <remarks>
    /// 正典把震屏列为第一版必须有的打击反馈，同时要求它可关 —— 它是最容易引起不适的一项。
    /// 关掉的判据不是「幅度调小」而是**恒零位移**：调小仍会动，而对晕动敏感的玩家要的是不动。
    /// 设置界面归后续那一组，本条只保证这个开关存在且真的能关死。
    /// </remarks>
    public bool ShakeEnabled { get; set; } = true;

    /// <summary>演出脚本是否正握着这台相机。</summary>
    public bool IsUnderCutsceneControl => _cutscene is { Released: false };

    /// <summary>当前接管者写下的用途，没人接管时为空 —— 卡住时能看出是谁没归还。</summary>
    public string CutsceneReason => IsUnderCutsceneControl ? _cutscene!.Reason : "";

    /// <summary>设地图边界，世界像素。相机的可视范围不许越出它。</summary>
    public void SetWorldBounds(int minX, int minY, int width, int height)
    {
        _world = Bounds.Create(minX, minY, width, height);
        Reclamp();
    }

    /// <summary>
    /// 设可建造区，世界像素。边缘推镜按它触发。
    /// </summary>
    /// <remarks>
    /// **与地图边界是两个矩形，不能合成一个。** 可建造区 40×30 格 = 640×480 世界像素，而俯视
    /// 1 倍缩放下逻辑视野宽度已经是 640 的下限 —— 拿可建造区当钳制边界的话横向根本不存在滚动，
    /// 宽窗口上视野还会比它更宽。所以钳制用地图（可建造区加周边地形），推镜用可建造区。
    /// 尺寸从配置读（PRD 的 `FR-24`），不写死 40×30。
    /// </remarks>
    public void SetBuildableArea(int minX, int minY, int width, int height)
    {
        _buildable = Bounds.Create(minX, minY, width, height);
    }

    /// <summary>把相机直接对准某点。演出接管期间禁止 —— 那时只能经凭据驱动。</summary>
    public void SnapTo(int x, int y)
    {
        RefuseWhileLeased();
        SnapToInternal(x, y);
    }

    /// <summary>
    /// 带死区地跟随一个目标。返回镜头是否真的动了。
    /// </summary>
    /// <remarks>
    /// 死区是**硬死区**：目标出了死区，镜头就移动到「目标刚好贴在死区边上」的位置，不多也不少。
    /// 这样镜头位移完全由目标位移决定，行为可预期；而且没有平滑系数，也就没有第三个要实机调的数。
    ///
    /// 演出接管期间是空转（返回 <c>false</c>）而不是排队：排队会让归还那一瞬间镜头猛地补上
    /// 整段位移。归还后的第一次调用会**直接对准目标**，理由见 <see cref="CameraCutscene.Release"/>。
    /// </remarks>
    public bool Follow(int targetX, int targetY)
    {
        if (IsUnderCutsceneControl)
        {
            return false;
        }

        if (_resnapOnNextFollow)
        {
            _resnapOnNextFollow = false;
            var beforeX = _centerX;
            var beforeY = _centerY;
            SnapToInternal(targetX, targetY);
            return _centerX != beforeX || _centerY != beforeY;
        }

        var wantX = FollowAxis(_centerX, targetX, DeadzoneHalfWidth);
        var wantY = FollowAxis(_centerY, targetY, DeadzoneHalfHeight);
        return MoveTo(wantX, wantY);
    }

    /// <summary>
    /// 建造时手动滚动镜头，按世界像素／秒。返回镜头是否真的动了。
    /// </summary>
    /// <remarks>
    /// 方向分量取 −1／0／+1，速度与时长由调用方乘出来 —— 输入来自 <c>InputRouter</c>，
    /// 规则层不认识输入。不足一像素的部分留在余量里累加，所以慢速滚动也不会卡住不动。
    /// </remarks>
    public bool Scroll(double deltaX, double deltaY)
    {
        if (IsUnderCutsceneControl)
        {
            return false;
        }

        return Drift(deltaX, deltaY);
    }

    /// <summary>
    /// 角色靠近可建造区边缘时自动推镜。返回镜头是否真的动了。
    /// </summary>
    /// <remarks>
    /// 作者 2026-08-30 定：建造**不做缩放**，靠滚动与推镜解决取景。所以这里是唯一让镜头自己
    /// 离开玩家滚到的位置的地方，触发条件写得保守：角色到可建造区某条边的距离不足
    /// <see cref="CameraFeel.EdgePushMarginCells"/> 格时，镜头朝那条边推，让玩家看清边界外还有
    /// 多少地方。推速比手动滚动慢，因为它是提示而不是操作。
    /// </remarks>
    public bool PushFromEdge(int actorX, int actorY, double seconds)
    {
        if (IsUnderCutsceneControl)
        {
            return false;
        }

        if (_buildable is not { } area)
        {
            throw new InvalidOperationException(
                "边缘推镜要先设可建造区（SetBuildableArea）—— 没设就推是在猜边界在哪");
        }

        var margin = CameraFeel.EdgePushMarginPixels;
        var step = CameraFeel.EdgePushPixelsPerSecond * seconds;
        var dirX = EdgeDirection(actorX, area.MinX, area.MaxX, margin);
        var dirY = EdgeDirection(actorY, area.MinY, area.MaxY, margin);
        if (dirX == 0 && dirY == 0)
        {
            return false;
        }

        return Drift(dirX * step, dirY * step);
    }

    /// <summary>
    /// 请求一次屏幕震动。关掉震动时这次请求不产生任何位移。
    /// </summary>
    /// <param name="amplitudeScreenPx">幅度，屏幕像素。必须能被 <see cref="Zoom"/> 整除。</param>
    /// <param name="seconds">时长，秒。</param>
    public void Shake(int amplitudeScreenPx, double seconds)
    {
        if (amplitudeScreenPx < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitudeScreenPx),
                $"震动幅度不能为负：{amplitudeScreenPx}");
        }

        if (amplitudeScreenPx % Zoom != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitudeScreenPx),
                $"震动幅度 {amplitudeScreenPx} 屏幕像素除不尽缩放 {Zoom} —— " +
                $"换算到世界像素会带半像素，最近邻采样下像素块会被切成宽窄不一的条");
        }

        _shakeAmplitudeScreenPx = amplitudeScreenPx;
        _shakeSeconds = seconds;
        _shakeElapsed = 0.0;
    }

    /// <summary>用默认幅度与时长震一次（重击）。</summary>
    public void Shake() => Shake(CameraFeel.ShakeAmplitudeScreenPx, CameraFeel.ShakeSeconds);

    /// <summary>推进震动时间线。每帧调一次。</summary>
    public void Advance(double seconds)
    {
        if (_shakeElapsed < _shakeSeconds)
        {
            _shakeElapsed += seconds;
        }
    }

    /// <summary>震动还在进行中吗。</summary>
    public bool IsShaking => ShakeEnabled && _shakeElapsed < _shakeSeconds;

    /// <summary>
    /// 演出脚本接管相机，拿到一张归还凭据。
    /// </summary>
    /// <remarks>
    /// **接口形状是有客观优劣的技术选择，理由写在这里**（`UI-5` 不问作者的那部分）。
    /// 被放弃的两种做法与放弃理由：
    ///
    /// 一是**一个布尔标志**（`IsCutscene = true` … `= false`）。它不阻止忘记复位，也分不清
    /// 「两段演出同时接管」和「一段演出接管了两次」；归还后的状态是「碰巧剩下什么」，而验收要求
    /// 的是「与接管前一致」。
    ///
    /// 二是**调用方自己存快照再还原**（`var s = rig.Snapshot(); … rig.Restore(s)`）。比标志好，
    /// 但快照会丢：跨帧的演出得把它存成字段，而存漏了不报错。
    ///
    /// 选**凭据**：接管那一刻由相机自己存下行为参数，归还时由相机自己还原，演出脚本记不住也没关系；
    /// 凭据实现 <see cref="IDisposable"/>，所以 <c>using</c> 能让抛异常的演出也归还 —— 演出脚本
    /// 最容易出错的地方正是中途抛；重复接管**抛异常**而不是静默覆盖，因为第二段演出悄悄顶掉第一段
    /// 之后，镜头会归还到错的状态而没人知道；接管期间 <see cref="Follow"/> 与 <see cref="SnapTo"/>
    /// 都不许直接驱动镜头，要动只能经凭据 —— 于是「谁在开镜头」在类型上就是明确的。
    /// </remarks>
    /// <param name="reason">这段演出是干什么的。卡住时它是唯一能指认责任方的东西。</param>
    public CameraCutscene TakeOver(string reason)
    {
        if (IsUnderCutsceneControl)
        {
            throw new InvalidOperationException(
                $"相机已被「{_cutscene!.Reason}」接管，不能再接管一次（「{reason}」）—— " +
                $"静默覆盖会让镜头归还到错的状态");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("接管必须写明用途", nameof(reason));
        }

        _cutscene = new CameraCutscene(this, reason, Snapshot.Of(this));
        return _cutscene;
    }

    // ── 内部 ────────────────────────────────────────────────────────────

    /// <summary>目标出了死区就把镜头挪到「目标刚好贴在死区边上」。**每轴独立，两轴逐字相同。**</summary>
    internal static int FollowAxis(int camera, int target, int deadzoneHalf)
    {
        if (target > camera + deadzoneHalf)
        {
            return target - deadzoneHalf;
        }

        return target < camera - deadzoneHalf ? target + deadzoneHalf : camera;
    }

    /// <summary>
    /// 把镜头中心钳进边界，让可视范围不越出地图。
    /// </summary>
    /// <remarks>
    /// **视野比地图还宽时居中而不是钳制。** 这不是补丁：`expand` 下逻辑宽度会撑开，而基地的
    /// 可建造区只有 640 世界像素宽，宽窗口上视野真的会比它宽。那时任何钳制都必然露白，居中至少
    /// 让露白对称、看起来是有意的。硬钳会把镜头顶到一边，白边全出现在另一边。
    /// </remarks>
    internal static int ClampAxis(int camera, int visible, int boundsMin, int boundsSize)
    {
        if (visible >= boundsSize)
        {
            return boundsMin + boundsSize / 2;
        }

        var half = visible / 2;
        return Math.Clamp(camera, boundsMin + half, boundsMin + boundsSize - (visible - half));
    }

    /// <summary>角色离哪条边不足余量：−1 靠近小的那头，+1 靠近大的那头，0 都不靠近。</summary>
    internal static int EdgeDirection(int actor, int min, int max, int margin)
    {
        var toMin = actor - min;
        var toMax = max - actor;
        if (toMin < margin && toMin <= toMax)
        {
            return -1;
        }

        return toMax < margin ? 1 : 0;
    }

    private static int CeilDiv(int value, int divisor) => (value + divisor - 1) / divisor;

    private void SnapToInternal(int x, int y)
    {
        _centerX = x;
        _centerY = y;
        _remainderX = 0.0;
        _remainderY = 0.0;
        Reclamp();
    }

    private bool MoveTo(int x, int y)
    {
        var beforeX = _centerX;
        var beforeY = _centerY;
        _centerX = x;
        _centerY = y;
        Reclamp();
        return _centerX != beforeX || _centerY != beforeY;
    }

    /// <summary>按速度漂移。不足一像素的部分留在余量里，所以慢速也不会卡死不动。</summary>
    private bool Drift(double deltaX, double deltaY)
    {
        _remainderX += deltaX;
        _remainderY += deltaY;
        var stepX = (int)Math.Truncate(_remainderX);
        var stepY = (int)Math.Truncate(_remainderY);
        if (stepX == 0 && stepY == 0)
        {
            return false;
        }

        _remainderX -= stepX;
        _remainderY -= stepY;
        return MoveTo(_centerX + stepX, _centerY + stepY);
    }

    private void Reclamp()
    {
        if (_world is not { } w)
        {
            return;
        }

        var clampedX = ClampAxis(_centerX, VisibleWidth, w.MinX, w.Width);
        var clampedY = ClampAxis(_centerY, VisibleHeight, w.MinY, w.Height);
        if (clampedX != _centerX)
        {
            _remainderX = 0.0;      // 顶到边就把余量清掉，否则松开方向后镜头还会自己蹭一下
        }

        if (clampedY != _centerY)
        {
            _remainderY = 0.0;
        }

        _centerX = clampedX;
        _centerY = clampedY;
    }

    private int ShakeAxis(int phase)
    {
        if (!ShakeEnabled || _shakeElapsed >= _shakeSeconds || _shakeSeconds <= 0.0)
        {
            return 0;
        }

        // 幅度线性衰减到 0，方向按固定频率换向。**波形是占位的**（`UI-12` 实机收敛时再定），
        // 这里要保住的不变量只有两条：位移恒为整数世界像素，关掉时恒为 0。
        // 两轴换向速率差一倍，于是四步走遍四个象限 —— 同步换向的话只会沿一条对角线抖。
        var remaining = 1.0 - (_shakeElapsed / _shakeSeconds);
        var amplitudeWorld = (double)_shakeAmplitudeScreenPx / Zoom;
        var magnitude = (int)Math.Round(amplitudeWorld * remaining, MidpointRounding.AwayFromZero);
        if (magnitude == 0)
        {
            return 0;
        }

        var step = (int)(_shakeElapsed * CameraFeel.ShakeHertz);
        var flip = phase == 0 ? step : step / 2;
        return flip % 2 == 0 ? magnitude : -magnitude;
    }

    private void RefuseWhileLeased()
    {
        if (IsUnderCutsceneControl)
        {
            throw new InvalidOperationException(
                $"相机正被「{_cutscene!.Reason}」接管，要动镜头请经凭据 —— " +
                $"绕过凭据改位置会让归还后的状态与接管前不一致");
        }
    }

    internal void DriveFromCutscene(CameraCutscene lease, int x, int y)
    {
        if (!ReferenceEquals(_cutscene, lease) || lease.Released)
        {
            throw new InvalidOperationException("这张凭据已经归还或不是当前接管者，不能再驱动相机");
        }

        SnapToInternal(x, y);
    }

    internal void OverrideZoomFromCutscene(CameraCutscene lease, int zoom)
    {
        if (!ReferenceEquals(_cutscene, lease) || lease.Released)
        {
            throw new InvalidOperationException("这张凭据已经归还或不是当前接管者，不能再改缩放");
        }

        if (zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom),
                $"演出缩放必须是正整数：{zoom} —— 非整数缩放会让像素变形");
        }

        _zoomOverride = zoom;
        Reclamp();
    }

    internal void ReleaseCutscene(CameraCutscene lease)
    {
        if (!ReferenceEquals(_cutscene, lease))
        {
            return;
        }

        lease.Saved.RestoreTo(this);
        _cutscene = null;

        // 归还后**直接对准跟随目标**，不做补间。演出可能把镜头带到很远的地方，而死区跟随是硬的：
        // 不重新对准的话第一帧会补上整段位移，看起来就是一次莫名的横移。想要软归还的演出应当
        // 自己先把镜头摇回来再归还 —— 那时它还握着凭据，做得到。
        _resnapOnNextFollow = true;
    }

    /// <summary>钳制用的矩形。世界像素，左上角加尺寸。</summary>
    internal readonly record struct Bounds(int MinX, int MinY, int Width, int Height)
    {
        internal int MaxX => MinX + Width;

        internal int MaxY => MinY + Height;

        internal static Bounds Create(int minX, int minY, int width, int height) =>
            width > 0 && height > 0
                ? new Bounds(minX, minY, width, height)
                : throw new ArgumentOutOfRangeException(
                    nameof(width), $"边界尺寸必须为正：{width}×{height}");
    }

    /// <summary>
    /// 接管那一刻的**行为参数**快照。刻意不含相机位置 —— 验收要求的是「跟随行为一致」。
    /// </summary>
    internal readonly record struct Snapshot(
        int ZoomOverride, bool ShakeEnabled, Bounds? World, Bounds? Buildable)
    {
        internal static Snapshot Of(CameraRig rig) =>
            new(rig._zoomOverride, rig.ShakeEnabled, rig._world, rig._buildable);

        internal void RestoreTo(CameraRig rig)
        {
            rig._zoomOverride = ZoomOverride;
            rig.ShakeEnabled = ShakeEnabled;
            rig._world = World;
            rig._buildable = Buildable;

            // 还原缩放与边界之后立刻重钳：演出可能把镜头带到还原后的边界之外，
            // 而不重钳的话那一帧的视口就越界了（表现为地图边上闪一下白）。
            rig.Reclamp();
        }
    }
}

/// <summary>
/// 演出接管相机的凭据（`UI-5`）。**归还是它的责任，不是演出脚本记性的责任。**
/// </summary>
/// <remarks>
/// 形状的取舍写在 <see cref="CameraRig.TakeOver"/> 的注释里。这里只补两条使用约定：
/// 归还是**幂等**的，重复调 <see cref="Release"/> 不抛；<see cref="Dispose"/> 就是
/// <see cref="Release"/>，所以跨不了帧的短演出可以直接 <c>using</c>。
/// </remarks>
public sealed class CameraCutscene : IDisposable
{
    private readonly CameraRig _rig;

    internal CameraCutscene(CameraRig rig, string reason, CameraRig.Snapshot saved)
    {
        _rig = rig;
        Reason = reason;
        Saved = saved;
    }

    /// <summary>这段演出是干什么的。</summary>
    public string Reason { get; }

    /// <summary>已经归还了吗。</summary>
    public bool Released { get; private set; }

    internal CameraRig.Snapshot Saved { get; }

    /// <summary>演出期间把镜头放到某处，整数世界像素。</summary>
    public void MoveTo(int x, int y) => _rig.DriveFromCutscene(this, x, y);

    /// <summary>演出期间临时改缩放。**只接受正整数** —— 非整数缩放会让像素变形。</summary>
    public void OverrideZoom(int zoom) => _rig.OverrideZoomFromCutscene(this, zoom);

    /// <summary>
    /// 归还相机：还原接管那一刻的行为参数，并让下一次跟随直接对准目标。
    /// </summary>
    public void Release()
    {
        if (Released)
        {
            return;
        }

        Released = true;
        _rig.ReleaseCutscene(this);
    }

    /// <inheritdoc cref="Release"/>
    public void Dispose() => Release();
}
