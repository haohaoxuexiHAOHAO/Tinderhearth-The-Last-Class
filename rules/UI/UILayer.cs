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

    /// <summary>即时提示：一个动作被拒时屏上那一行字。不接管输入、不进导航栈。</summary>
    /// <remarks>
    /// 它在面板与弹窗之上：面板开着时被拒的那一下（背包页里丢不掉那一样）也要看得见，而常驻显示那一层在面板开着时
    /// 整层藏起来。它在幕布之下：淡入淡出盖住一切，也盖住它。
    /// </remarks>
    Notice = 45,

    /// <summary>遮罩层：淡入淡出与场景切换时盖住一切。</summary>
    /// <remarks>
    /// 刻意不叫「过场」。那个词在设计仓 design/剧情演出系统.md 里指的是剧情演出，而本层只是
    /// 一块盖住画面的色块。两个东西同名，照界面文档找过来的人会把这一层读成演出层。
    /// </remarks>
    Curtain = 50,
}

/// <summary>层级上的那一条判定：世界暂停的时候，这一层跟不跟着停。</summary>
public static class UILayerExtensions
{
    /// <summary>
    /// 世界暂停时这一层照不照跑：接过输入的那几层照跑，画在世界里的与常驻显示的跟着世界停。
    /// </summary>
    /// <remarks>
    /// 它答的是「暂停期间谁还在动」，不是「谁会让世界暂停」—— 后一件只看导航栈里有没有层
    /// （<see cref="NavigationStack.WorldShouldPause"/>）。
    ///
    /// 世界暂停是因为有一层界面接过了输入，所以接过输入的那几层自己不能停：停了就是手环一打开就点不动、
    /// 按返回也关不掉，而那不报错。面板与弹窗照跑。幕布也照跑：淡入淡出与转场不该被一次暂停冻在半路。
    ///
    /// 世界空间那一层画在执行者身上，跟世界一起停，不然世界冻住了读条还在涨、伤害数字还在飘。常驻显示也跟着停：
    /// 面板接过输入的时候它不该再听按键（随身栏是每帧问门面有没有换格的）。它显示的数是有人塞进去的、不靠每帧去取，
    /// 所以停着照样刷新得了。
    ///
    /// 即时提示那一层不接管输入，却照跑：面板开着时世界停着，跟着停的话面板里被拒的那一行就一直挂着、收不起来。
    ///
    /// 暂停的判据在设计仓 canon/gameplay/玩法定位.md 的「弹界面接管输入就暂停世界，世界里的动作不暂停」。
    /// </remarks>
    public static bool RunsWhilePaused(this UILayer layer) => layer switch
    {
        UILayer.World or UILayer.WorldSpace or UILayer.Hud => false,
        UILayer.Panel or UILayer.Dialog or UILayer.Notice or UILayer.Curtain => true,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), $"没有这一层：{layer}"),
    };
}
