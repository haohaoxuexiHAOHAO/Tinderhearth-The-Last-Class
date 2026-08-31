using Godot;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>
/// 全游戏唯一的相机节点（`UI-5`）。**两种视角共用它，视角只是构造参数。**
/// </summary>
/// <remarks>
/// 分工与 <see cref="UiRoot"/> 一致：**判定在规则层，节点在这里。** 跟随、钳制、震动、演出接管
/// 与推镜的全部几何都在 <see cref="CameraRig"/> 里，有不启引擎的单元测试盯着；本类只做三件
/// 引擎才能做的事 —— 每帧把真实逻辑视口尺寸喂给规则层、把算出来的整数世界坐标抄进
/// <c>Camera2D</c>、把输入经 <see cref="InputRouter"/> 翻成滚动方向。
///
/// **为什么 <c>sealed</c>。** 正典点名「行为不定下来，两种视角会各写一套」，而「各写一套」最自然
/// 的形态就是给这个类派两个子类。封起来让那条路在编译期就走不通；`tools/check_camera.py` 另有
/// 一条静态判据核「引擎层只有一个 <c>Camera2D</c> 派生类型」，两道一起才拦得住新建一个平级类。
///
/// **刻意不用引擎内置的 <c>Limit*</c> 钳制。** 两个理由：一是它不启引擎就测不了，而钳制少一边
/// 的表现是「地图边上偶尔露白」，属于要凑巧遇到才看见的那类失效；二是 `aspect="expand"` 会撑出
/// 「视野比地图还宽」的局面，内置钳制在那时会把镜头顶到一边、白边全挤到另一侧，而规则层那份
/// 显式改成居中。<c>Limit*</c> 因此保持默认值，`check_camera.py` 会核它们没被启用 ——
/// 两份钳制同时生效会互相拉扯。
///
/// **不碰 <c>TextureFilter</c>。** 它可逐节点覆盖并向下继承，而 `canvas_items` 下 12px 中文
/// 清晰唯一依靠项目级的最近邻过滤（`UI-4` 实测）。相机是所有世界内容的祖先，在这里手滑等于
/// 一次毁掉整棵子树（守卫归 `ENG-13`）。
/// </remarks>
public sealed partial class GameCamera : Camera2D
{
    public GameCamera(CameraView view)
    {
        Rig = new CameraRig(view);
        Name = $"GameCamera{view}";
    }

    /// <summary>相机的全部行为都在这里。引擎层只抄结果，不自己算。</summary>
    public CameraRig Rig { get; }

    /// <summary>跟随谁。为空时相机不跟随（演出、建造或还没生成主角）。</summary>
    public Node2D? FollowTarget { get; set; }

    /// <summary>
    /// 建造模式：跟随换成「手动滚动 + 角色靠近可建造区边缘时推镜」。
    /// </summary>
    /// <remarks>
    /// 作者 2026-08-30 定建造**不做缩放**，所以这里没有任何改缩放的分支。**基地场景与建造界面
    /// 本身不在 `UI-5`** —— 本类只交出滚动与推镜这两项能力，以及可建造区尺寸从配置读这件事。
    /// </remarks>
    public bool BuildMode { get; set; }

    /// <summary>
    /// 输入门面。建造滚动经它问输入，**不直接调 <c>Input.IsActionPressed</c>**。
    /// </summary>
    /// <remarks>
    /// `UI-7` 实测：`SetInputAsHandled` 之后引擎的轮询状态**没有**被清掉，所以直接轮询的相机会在
    /// 玩家按住扳机挑技能时照旧滚动。`tools/check_input_map.py` 会扫 `src/` 判失败。
    /// </remarks>
    public InputRouter? Router { get; set; }

    public override void _Ready()
    {
        // 位置平滑必须关：它会算出分数位置，而分数位置下最近邻采样把像素块切成宽窄不一的条
        // （`UI-5` 实测，判据在 CameraProbe 里）。要软化镜头请由演出脚本驱动。
        PositionSmoothingEnabled = false;
        RotationSmoothingEnabled = false;
        IgnoreRotation = true;
        AnchorMode = AnchorModeEnum.DragCenter;

        SyncViewport();
        Apply();
        MakeCurrent();
    }

    public override void _Process(double delta)
    {
        SyncViewport();
        Rig.Advance(delta);

        if (!Rig.IsUnderCutsceneControl)
        {
            if (BuildMode)
            {
                DriveBuildMode(delta);
            }
            else if (FollowTarget is { } target)
            {
                var at = target.GlobalPosition;
                Rig.Follow(RoundToPixel(at.X), RoundToPixel(at.Y));
            }
        }

        Apply();
    }

    /// <summary>把可建造区尺寸从配置装进相机。**格数由调用方给，本类不认识 40 与 30。**</summary>
    public void UseBuildableArea(int widthCells, int heightCells, int originX = 0, int originY = 0)
    {
        Rig.SetBuildableArea(originX, originY,
            widthCells * UiMetrics.BaseUnit, heightCells * UiMetrics.BaseUnit);
    }

    /// <summary>把规则层算出来的结果抄进节点。**每帧只有这里改 <c>Camera2D</c> 的属性。**</summary>
    public void Apply()
    {
        // 位置与震动分开放：位置是镜头去哪，Offset 是震动，混在一起就没法证明「关掉震动后零位移」。
        Position = new Vector2(Rig.CenterX, Rig.CenterY);
        Offset = new Vector2(Rig.ShakeOffsetX, Rig.ShakeOffsetY);
        Zoom = new Vector2(Rig.Zoom, Rig.Zoom);
    }

    /// <summary>
    /// 把**真实**逻辑视口尺寸喂给规则层。
    /// </summary>
    /// <remarks>
    /// 不能缓存成 640×360：`expand` 下逻辑宽度是下限，会随窗口宽高比撑开（`UI-3` 实测
    /// 3840×2130 的窗口得到 649×360）。按 640 算钳制范围的话宽窗口上镜头会停得太早、边缘露白。
    /// </remarks>
    private void SyncViewport()
    {
        var size = GetViewport().GetVisibleRect().Size;
        Rig.SetLogicalViewport(Mathf.CeilToInt(size.X), Mathf.CeilToInt(size.Y));
    }

    private void DriveBuildMode(double delta)
    {
        if (Router is { } router)
        {
            var axis = router.MoveDirection();
            if (axis != Vector2.Zero)
            {
                var step = CameraFeel.ScrollPixelsPerSecond * delta;
                Rig.Scroll(axis.X * step, axis.Y * step);
            }
        }

        if (FollowTarget is { } actor)
        {
            var at = actor.GlobalPosition;
            Rig.PushFromEdge(RoundToPixel(at.X), RoundToPixel(at.Y), delta);
        }
    }

    /// <summary>
    /// 世界坐标取整到整数像素。**唯一的取整点就在这里。**
    /// </summary>
    /// <remarks>
    /// 角色位置是浮点的（物理与移动都用浮点），相机中心必须是整数。把取整放在进入规则层的这一处，
    /// 「相机位置恒为整数」就是结构保证而不是某一行四舍五入的结果。
    /// </remarks>
    private static int RoundToPixel(float value) => Mathf.RoundToInt(value);
}
