using Godot;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>全游戏唯一的相机节点。两种视角共用它，视角只是构造参数。</summary>
/// <remarks>
/// 跟随、钳制、震动与推镜的几何都在 <see cref="CameraRig"/> 里，那边不启引擎、有单元测试盯着。
/// 本类只做三件引擎才能做的事：把真实的逻辑视口尺寸喂给规则层、把算出来的整数坐标抄进节点、
/// 把输入经 <see cref="InputRouter"/> 翻成滚动方向。<c>sealed</c> 堵的是「每种视角各派一个子类」。
///
/// 刻意不用引擎内置的 <c>Limit*</c> 钳制，那几个属性必须留着默认值：在编辑器里给相机填 Limit 会
/// 静默破坏规则层那份钳制，两份同时生效会互相拉扯。而且窗口按 expand 撑开时会出现「视野比地图还
/// 宽」的局面，内置钳制那时会把镜头顶到一边、白边全挤到另一侧，规则层那份则显式改成居中。
///
/// 不碰 <c>TextureFilter</c>：它能逐节点覆盖并向下继承，而 12 像素的中文看得清全靠项目里那一次
/// 最近邻过滤设置；相机是所有世界内容的祖先，在这里手滑等于一次毁掉整棵子树。
/// </remarks>
public sealed partial class GameCamera : Camera2D
{
    public GameCamera(CameraView view)
    {
        Rig = new CameraRig(view);
        Name = $"GameCamera{view}";
    }

    /// <summary>相机的全部行为都在这里算。本节点只抄结果，不自己算。</summary>
    public CameraRig Rig { get; }

    /// <summary>跟随谁。为空时相机不跟随（演出中、建造中，或还没生成主角）。</summary>
    public Node2D? FollowTarget { get; set; }

    /// <summary>建造模式：跟随换成手动滚动，外加角色靠近可建造区边缘时推镜。</summary>
    /// <remarks>
    /// 建造不做缩放，所以这里没有任何改缩放的分支。本类只交出滚动与推镜这两项能力，
    /// 基地场景与建造界面本身不在这里。
    /// </remarks>
    public bool BuildMode { get; set; }

    /// <summary>输入门面。建造滚动经它问输入，不直接调 <c>Input.IsActionPressed</c>。</summary>
    /// <remarks>
    /// 实测：调过 <c>SetInputAsHandled</c> 之后引擎的轮询状态并没有被清掉，所以自己轮询的相机会在
    /// 玩家按住扳机挑技能时照旧滚动。走门面就不会。
    /// </remarks>
    public InputRouter? Router { get; set; }

    public override void _Ready()
    {
        // 位置平滑必须关：它会算出带小数的位置，而那时最近邻采样会把像素块切成宽窄不一的条
        // （实测过）。要让镜头软一点，请由演出脚本驱动。
        PositionSmoothingEnabled = false;
        RotationSmoothingEnabled = false;
        IgnoreRotation = true;
        AnchorMode = AnchorModeEnum.DragCenter;

        SyncViewport();
        Apply();
        MakeCurrent();
    }

    /// <summary>由场景自己调 <see cref="Advance"/> 时置真，渲染帧里就不再自行推进一遍。</summary>
    public bool ManualAdvance { get; init; }

    public override void _Process(double delta)
    {
        if (!ManualAdvance) Advance(delta);
    }

    /// <summary>推进一帧：跟随、震动衰减，再把结果写回节点。</summary>
    public void Advance(double delta)
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

    /// <summary>告诉相机可建造区有多大，单位是格。格数由调用方给，本类不写死任何一个格数。</summary>
    public void UseBuildableArea(int widthCells, int heightCells, int originX = 0, int originY = 0)
    {
        Rig.SetBuildableArea(originX, originY,
            widthCells * UIMetrics.BaseUnit, heightCells * UIMetrics.BaseUnit);
    }

    /// <summary>把算出来的结果抄进节点。每帧只有这里会改 <c>Camera2D</c> 的属性。</summary>
    public void Apply()
    {
        // 位置与震动分开放：Position 是镜头去哪，Offset 是这一下的抖动。混在一处就再也分不出
        // 「镜头本来该在哪」，关掉震动之后也证明不了位移归零。
        Position = new Vector2(Rig.CenterX, Rig.CenterY);
        Offset = new Vector2(Rig.ShakeOffsetX, Rig.ShakeOffsetY);
        Zoom = new Vector2(Rig.Zoom, Rig.Zoom);
    }

    /// <summary>每帧把真实的逻辑视口尺寸喂给规则层，单位是逻辑像素。</summary>
    /// <remarks>
    /// 不能把它当成固定值缓存起来：窗口按 expand 撑开时，项目里设的逻辑宽度只是下限，实际宽度会
    /// 随窗口宽高比变大（实测过一个很宽的窗口撑出 649 的逻辑宽度）。按下限算钳制范围的话，宽窗口
    /// 上镜头会停得太早、地图边上露白。
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

    /// <summary>把世界坐标四舍五入到整数像素。全类只有这一处取整。</summary>
    /// <remarks>
    /// 角色位置是浮点的（物理与移动都用浮点），而相机中心必须落在整数像素上，否则画面会抖。
    /// 取整只放在进规则层这一个入口，「相机位置永远是整数」就是接口形状的结果，不用靠人记得。
    /// </remarks>
    private static int RoundToPixel(float value) => Mathf.RoundToInt(value);
}
