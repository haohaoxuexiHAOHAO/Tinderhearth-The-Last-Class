namespace Tinderhearth.Rules.UI;

/// <summary>
/// 界面层级，自下而上。层号只在这里定义，各场景不自己挑。
/// </summary>
/// <remarks>
/// 钉死顺序是为了不出现「弹窗被 HUD 挡住」这类问题。各界面自己挑层号的话，真出了问题要翻遍
/// 所有场景才知道谁用了哪个号。
///
/// <see cref="WorldSpace"/> 单独成一层，因为读条与精英血条要跟着角色在世界里移动、随相机缩放，
/// 与屏幕空间的 HUD 是两套定位方式。混进 HUD 层就会得到「读条固定在屏幕左上角」那种错。
///
/// 层号之间留 10 的间隔：将来插一层（例如提示气泡）不必给全部层重新编号。
/// </remarks>
public enum UILayer
{
    /// <summary>世界本身：图块、角色、场景物件。</summary>
    World = 0,

    /// <summary>世界空间 UI：读条、精英血条、气泡。跟随相机，随缩放变化。</summary>
    WorldSpace = 10,

    /// <summary>常驻抬头显示：资源、技能位、目标进度、队友状态。</summary>
    Hud = 20,

    /// <summary>面板：背包、手环、名册、角色面板。可叠放，由导航栈管。</summary>
    Panel = 30,

    /// <summary>弹窗：确认、每日结算摘要、错误提示。永远在面板之上。</summary>
    Dialog = 40,

    /// <summary>遮罩层：淡入淡出与场景切换时盖住一切。</summary>
    /// <remarks>
    /// 刻意不叫「过场」。那个词在设计仓 design/剧情演出系统.md 里指的是剧情演出，而本层只是
    /// 一块盖住画面的色块。两个东西同名，照界面文档找过来的人会把这一层读成演出层。
    /// </remarks>
    Curtain = 50,
}
