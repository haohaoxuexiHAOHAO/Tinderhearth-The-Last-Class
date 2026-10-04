namespace Tinderhearth.Rules.UI;

/// <summary>视角。只有这两种，而且共用同一份相机实现，没有例外。</summary>
public enum CameraView
{
    /// <summary>基地与城区。1 倍缩放。</summary>
    TopDown,

    /// <summary>战斗与出征关卡。2 倍整数缩放，有效视野是逻辑分辨率的一半。</summary>
    SideView,
}

/// <summary>一台相机的全部行为：跟随、钳制、震动、演出接管、建造滚动与边缘推镜。</summary>
/// <remarks>
/// 两种视角共用这一个类，视角只是构造参数。这样做是为了防「两种视角各写一套相机」。视角在这里
/// 只影响 <see cref="Zoom"/> 以及由它换算出来的可视尺寸和死区，其余判定逐字相同。引擎那一侧
/// <c>GameCamera</c> 是 <c>sealed</c> 的，所以再派一个子类在编译期就走不通。
///
/// 整台状态机放在规则层，因为相机的失效方式都不报错：死区写成 0 只表现为镜头有点抖，钳制少一边
/// 只表现为地图边上偶尔露白，演出结束忘了归还只表现为后面镜头不动了。这些都是纯几何，放这里
/// 就能用不启引擎的单元测试盯住。引擎层只剩一件事：把算出来的中心和震动偏移抄进 <c>Camera2D</c>。
///
/// 对外暴露的位置量一律是整数世界像素。速度驱动的移动（滚动、推镜）把不足一像素的部分留在内部
/// 余量里累加，不四舍五入，这样「输出恒为整数」是结构保证而不是某一行取整的结果。实测过相机落在
/// 半像素上的后果：最近邻采样会把像素块切成宽窄不一的条，看起来只是画面有点脏，而且不报错。
/// </remarks>
public sealed class CameraRig
{
    private int _logicalWidth = UIMetrics.BaseWidth;
    private int _logicalHeight = UIMetrics.BaseHeight;

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

    /// <summary>整数缩放倍数。俯视是 1 倍，侧视取 <see cref="UIMetrics.SideViewZoom"/>。</summary>
    /// <remarks>
    /// 侧视那个倍数不在这里重定义，它是全项目的像素基准、已经在 <see cref="UIMetrics"/> 里，
    /// 那边有测试钉住它换算出来的有效视野。演出期间可以临时覆盖，但只能覆盖成正整数。
    /// </remarks>
    public int Zoom => _zoomOverride > 0
        ? _zoomOverride
        : View == CameraView.SideView ? UIMetrics.SideViewZoom : 1;

    /// <summary>告诉相机当前的逻辑视口尺寸。必须每帧传，不能缓存成常量。</summary>
    /// <remarks>
    /// 视口按 expand 拉伸时，逻辑宽度是个下限而不是定值：高度锁住，宽度按窗口宽高比撑开（实测
    /// 3840×2130 的窗口得到的逻辑尺寸是 649×360）。所以拿基准宽度去算钳制范围会错，宽窗口上
    /// 相机会停得太早，地图边缘露白。
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

    /// <summary>能看到的世界宽度，世界像素。向上取整，理由见 <see cref="VisibleHeight"/>。</summary>
    public int VisibleWidth => CeilDiv(_logicalWidth, Zoom);

    /// <summary>能看到的世界高度，世界像素。</summary>
    /// <remarks>
    /// 向上取整而不是截断，因为逻辑宽度可能是奇数（视口撑开时就会）。截断会让相机以为自己看得比
    /// 实际少半像素，于是钳制放得太松、边缘露出半像素的白缝。往大取只会让钳制更保守。
    /// </remarks>
    public int VisibleHeight => CeilDiv(_logicalHeight, Zoom);

    /// <summary>跟随死区的半宽，世界像素。屏幕像素除以缩放 —— 两种视角取景一致。</summary>
    public int DeadzoneHalfWidth => CameraFeel.DeadzoneHalfWidthScreenPx / Zoom;

    /// <summary>跟随死区的半高，世界像素。</summary>
    public int DeadzoneHalfHeight => CameraFeel.DeadzoneHalfHeightScreenPx / Zoom;

    /// <summary>钳制用的地图边界。没设过就不钳制（测试与演出场景可以不设）。</summary>
    public bool HasWorldBounds => _world is not null;

    /// <summary>装进相机的可建造区宽度，世界像素；没设过是 0。</summary>
    /// <remarks>
    /// 暴露出来是为了能和 <c>data/config/game.json</c> 里的值比对，于是「有人在代码里写死一个
    /// 尺寸」躲不过去。打印配置里的值只能证明配置读到了，证明不了装进相机的是那个值。
    /// </remarks>
    public int BuildableWidth => _buildable?.Width ?? 0;

    /// <inheritdoc cref="BuildableWidth"/>
    public int BuildableHeight => _buildable?.Height ?? 0;

    /// <summary>横向真的会被钳制吗。视野比地图还宽时不钳制而是居中，见 <see cref="Reclamp"/>。</summary>
    public bool ClampsHorizontally => _world is { } w && VisibleWidth < w.Width;

    /// <summary>纵向真的会被钳制吗。</summary>
    public bool ClampsVertically => _world is { } w && VisibleHeight < w.Height;

    /// <summary>相机中心的横坐标，整数世界像素，已经钳进边界了。</summary>
    public int CenterX => _centerX;

    /// <summary>相机中心的纵坐标，整数世界像素，已经钳进边界了。</summary>
    public int CenterY => _centerY;

    /// <summary>震动的横向位移，整数世界像素。关掉震动时恒为 0。</summary>
    public int ShakeOffsetX => ShakeAxis(phase: 0);

    /// <summary>震动的纵向位移，整数世界像素。关掉震动时恒为 0。</summary>
    public int ShakeOffsetY => ShakeAxis(phase: 1);

    /// <summary>屏幕震动的总开关。将来设置界面里那一项直接落在这里。</summary>
    /// <remarks>
    /// 震屏是第一版就要有的打击反馈，同时它必须可关，因为它是最容易引起不适的一项。关掉的标准
    /// 不是「幅度调小」而是位移恒为零：调小仍然会动，而对晕动敏感的玩家要的是不动。设置界面还
    /// 没有，这个开关先保证存在且真的关得死。
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

    /// <summary>设可建造区，世界像素。边缘推镜按它触发。</summary>
    /// <remarks>
    /// 它和地图边界是两个矩形，不能合成一个。可建造区是能盖房的那块地，而地图还含它之外的周边
    /// 地形（水面一类），玩家走得到那里。所以钳制按地图算、推镜按可建造区算。
    ///
    /// 合成一个的话两头都不对：按可建造区钳制，玩家走出去相机就卡住；按地图推镜，推镜会在盖不了
    /// 房的地方也触发。两个尺寸都从配置读，代码里一个都不写死。
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

    /// <summary>带死区地跟随一个目标。返回镜头是否真的动了。</summary>
    /// <remarks>
    /// 死区是硬的：目标出了死区，镜头就移动到「目标刚好贴在死区边上」的位置，不多也不少。这样
    /// 镜头位移完全由目标位移决定，行为可预期，而且没有平滑系数、也就少一个要实机调的数。
    ///
    /// 演出接管期间它空转、返回 <c>false</c>，不排队。排队会让归还那一瞬间镜头猛地补上整段位移。
    /// 归还后的第一次调用会直接对准目标，理由见 <see cref="CameraCutscene.Release"/>。
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

    /// <summary>建造时手动滚动镜头，参数是这一帧要走的世界像素。返回镜头是否真的动了。</summary>
    /// <remarks>
    /// 方向分量取 -1、0、+1，速度和时长由调用方乘出来，因为输入来自引擎层的输入门面、规则层
    /// 不认识输入。不足一像素的部分留在余量里累加，所以慢速滚动也不会卡住不动。
    /// </remarks>
    public bool Scroll(double deltaX, double deltaY)
    {
        if (IsUnderCutsceneControl)
        {
            return false;
        }

        return Drift(deltaX, deltaY);
    }

    /// <summary>角色靠近可建造区边缘时自动推镜。返回镜头是否真的动了。</summary>
    /// <remarks>
    /// 建造不做缩放，靠滚动和推镜解决取景。这里是唯一让镜头自己离开玩家滚到的位置的地方，所以
    /// 触发条件写得保守：角色到可建造区某条边的距离不足 <see cref="CameraFeel.EdgePushMarginCells"/>
    /// 格时镜头朝那条边推。推速比手动滚动慢，因为它是提示而不是操作。
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

    /// <summary>演出脚本接管相机，拿到一张归还凭据。</summary>
    /// <remarks>
    /// 被放弃的第一种做法是一个布尔标志（<c>IsCutscene = true</c> 然后 <c>= false</c>）。它不
    /// 阻止忘记复位，也分不清「两段演出同时接管」和「一段演出接管了两次」；归还后的状态是碰巧
    /// 剩下什么，而要的是与接管前一致。
    ///
    /// 第二种是调用方自己存快照再还原。比标志好，但快照会丢：跨帧的演出得把它存成字段，而存漏了
    /// 不报错。
    ///
    /// 所以选凭据：接管那一刻由相机自己存下行为参数、归还时自己还原，演出脚本记不住也没关系。
    /// 凭据实现 <see cref="IDisposable"/>，所以 <c>using</c> 能让中途抛异常的演出也归还。重复接管
    /// 抛异常而不是静默覆盖，因为第二段悄悄顶掉第一段之后镜头会归还到错的状态。接管期间
    /// <see cref="Follow"/> 和 <see cref="SnapTo"/> 都不许直接驱动镜头，要动只能经凭据。
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

    /// <summary>目标出了死区就把镜头挪到「目标刚好贴在死区边上」。每轴独立，两轴算法相同。</summary>
    internal static int FollowAxis(int camera, int target, int deadzoneHalf)
    {
        if (target > camera + deadzoneHalf)
        {
            return target - deadzoneHalf;
        }

        return target < camera - deadzoneHalf ? target + deadzoneHalf : camera;
    }

    /// <summary>把镜头中心钳进边界，让可视范围不越出地图。</summary>
    /// <remarks>
    /// 视野比地图还宽时居中，而不是钳制。这不是补丁：视口撑开时逻辑宽度会变大，宽窗口上视野真的
    /// 会比基地那块地还宽。那时任何钳制都必然露白，居中至少让露白左右对称、看起来是有意的；硬钳
    /// 会把镜头顶到一边，白边全出现在另一边。
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

        // 幅度线性衰减到 0，方向按固定频率换向。这个波形是临时的，手感要实机调；这里保住的只有
        // 两条：位移恒为整数世界像素，开关关掉时恒为 0。
        // 两轴换向速率差一倍，于是四步走遍四个象限。同步换向的话只会沿一条对角线抖。
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

        // 归还后直接对准跟随目标，不做补间。演出可能把镜头带到很远的地方，而死区跟随是硬的，
        // 不重新对准的话第一帧会补上整段位移，看起来就是一次莫名的横移。想要软归还的演出应当
        // 自己先把镜头摇回来再归还，那时它还握着凭据、做得到。
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

    /// <summary>接管那一刻的行为参数快照。它不含相机位置，要还原的是跟随行为、不是镜头在哪。</summary>
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

/// <summary>演出接管相机的凭据。归还是它的责任，不是演出脚本记性的责任。</summary>
/// <remarks>
/// 为什么用凭据而不是一个布尔标志，写在 <see cref="CameraRig.TakeOver"/> 的注释里。这里补两条
/// 使用约定：归还是幂等的，重复调 <see cref="Release"/> 不抛；<see cref="Dispose"/> 就是
/// <see cref="Release"/>，所以不跨帧的短演出可以直接 <c>using</c>。
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

    /// <summary>演出期间临时改缩放。只接受正整数，非整数缩放会让像素变形。</summary>
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
