namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 界面层级（`UI-6`）。自下而上固定六层，**一处定义，各场景不得自行摆放**。
/// </summary>
/// <remarks>
/// 为什么要钉死顺序：不钉的话每个界面自己挑一个层号，迟早出现「弹窗被 HUD 挡住」这类问题，
/// 而排查时你要翻遍所有场景才知道谁用了哪个号。
///
/// <see cref="WorldSpace"/> 单独成层，是因为正典要求[读条画在执行者身上而不是界面角落]、
/// 血条只给精英与 BOSS —— 这些元素必须跟着角色在世界里移动并随相机缩放，与屏幕空间的 HUD
/// 是两套定位机制。把它们混进 HUD 层，就会得到「读条固定在屏幕左上角」那种错。
///
/// 数值之间留 10 的间隔：将来插一层（例如提示气泡）不必给全部层重新编号。
/// </remarks>
public enum UiLayer
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

    /// <summary>过场遮罩：淡入淡出、场景切换。盖住一切。</summary>
    Curtain = 50,
}
