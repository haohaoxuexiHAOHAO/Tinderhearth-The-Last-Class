namespace Tinderhearth.Rules.Ui;

/// <summary>
/// 相机手感的**初值**（`UI-5`）。**这些数还没实机收敛过，归 `UI-12`。**
/// </summary>
/// <remarks>
/// 为什么这些数在这里而不在别处，三条都有理由：
///
/// **不进 `design/数值模型.md`。** 那份持有的是六组**平衡量**（属性、成长、判定、时间、经济、
/// 容量），每一项都进得了某条不等式判据、能被 `simulate_week.py` 重算。相机手感一条公式都不进，
/// 混进去只会把那份文档的可信度来源（改参数就重跑）稀释掉。
///
/// **不进 `data/config/game.json`。** 那里放「结构性容量」，判据是 mod 与联机都会撞的量。
/// 相机手感不是内容而是表现规则，正典的「内容外置，规则不外置」把它划在外面；而且外置意味着
/// mod 能把死区改成 0，那会让所有关卡在未知取景下运行。**可建造区尺寸是另一回事**，它是场景
/// 规模、确实从配置读（PRD 的 `FR-24`）。
///
/// **放规则层而不是引擎层**，理由与 <see cref="UiMetrics"/> 相同：它们之间有可以判死的关系 ——
/// 屏幕像素量必须能被侧视缩放整除，否则换算到世界像素就出现半像素，而半像素会破坏像素对齐
/// （已实测，见 `UI-5` 的实现笔记）。有测试盯着这些关系，改坏当场失败。
///
/// **量纲一律写在成员名里。** 死区与震动幅度用**屏幕像素**：两种视角共用同一个数，除以各自的
/// 缩放就得到世界像素，于是取景在两种视角下看起来一致 —— 这正是「两种视角共用同一份实现」在
/// 数值上的形态。推镜与滚动用**世界像素／秒**与**格**：它们描述的是在地图上走多快，与缩放无关。
/// </remarks>
public static class CameraFeel
{
    /// <summary>
    /// 跟随死区的半宽，屏幕像素。角色在死区内移动时镜头不动。
    /// </summary>
    /// <remarks>
    /// 受什么约束：侧视有效视野只有 320×180 世界像素，角色 32px 高占屏 64px。死区半宽取到
    /// 视野半宽（屏幕 320px）附近就意味着角色能贴到屏幕边缘而镜头还不动 —— 那时玩家看不见
    /// 自己要去的方向。上限按「角色始终留在画面中间那一半」取，即 ≤ 视野半宽的一半，屏幕 160
    /// 像素（侧视换算成 80 世界像素）；下限按「别小到每帧都拖着镜头」取，一个栅格（8 屏幕像素）。
    /// </remarks>
    public const int DeadzoneHalfWidthScreenPx = 48;

    /// <summary>跟随死区的半高，屏幕像素。竖向取得比横向小 —— 纵向视野本来就只有 180。</summary>
    public const int DeadzoneHalfHeightScreenPx = 32;

    /// <summary>
    /// 屏幕震动的幅度，屏幕像素。**必须能被侧视缩放整除**，否则换算成世界像素带半像素。
    /// </summary>
    /// <remarks>
    /// 正典只给重击与特定事件震屏，且要求可关。幅度上限按「不把角色晃出死区」取（≤ 死区半高）；
    /// 下限是一个世界像素乘缩放，也就是侧视下的 2 屏幕像素 —— 比这更小的位移在整数网格上表达不出来。
    /// </remarks>
    public const int ShakeAmplitudeScreenPx = 4;

    /// <summary>震动时长，秒。</summary>
    /// <remarks>
    /// 上限受正典那条约束的邻居管着：顿帧不能长到打断连段输入节奏，震动跟着顿帧走，超过四分之一
    /// 秒就会盖住下一次输入的反馈。下限是一个震动周期，低于它玩家只看到一次跳动而不是震动。
    /// </remarks>
    public const double ShakeSeconds = 0.12;

    /// <summary>震动换向频率，赫兹。不跟帧率绑 —— 帧率变化不该改变震动的读感。</summary>
    public const int ShakeHertz = 30;

    /// <summary>边缘推镜的触发余量，**格**。角色离可建造区边缘不足这么多格就开始推镜。</summary>
    /// <remarks>
    /// 用格而不是像素，因为它描述的是建造网格上的距离，玩家的心理单位也是格。上限受视野管着：
    /// 余量必须小于视野半宽（侧视 160 世界像素 = 10 格），否则一进场就在推镜。
    /// </remarks>
    public const int EdgePushMarginCells = 3;

    /// <summary>边缘推镜的速度，世界像素／秒。比手动滚动慢 —— 它是提示而不是操作。</summary>
    public const int EdgePushPixelsPerSecond = 128;

    /// <summary>建造时手动滚动的速度，世界像素／秒。</summary>
    /// <remarks>
    /// 判据是「横穿可建造区不该久到让人不耐烦」：640px 宽的可建造区按这个速度走完 2.5 秒。
    /// </remarks>
    public const int ScrollPixelsPerSecond = 256;

    /// <summary>边缘推镜触发余量换算成世界像素。</summary>
    public static int EdgePushMarginPixels => EdgePushMarginCells * UiMetrics.BaseUnit;
}
