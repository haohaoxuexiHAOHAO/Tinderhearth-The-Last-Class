namespace Tinderhearth.Rules.UI;

/// <summary>
/// 相机手感的初值。量纲一律写在成员名里：屏幕像素、世界像素每秒、格。
/// </summary>
/// <remarks>
/// 这几个数还没实机收敛过。先给一组跑得起来的初值，最终由作者在 Godot 里调。
///
/// 它们是表现规则不是玩法数值，所以既不进设计仓 design/数值模型.md（那份收的是进得了平衡算式
/// 的量），也不进 <c>data/config/game.json</c>（那是 mod 能改的内容，而把死区改成 0 会让所有
/// 关卡在谁也没见过的取景下运行）。可建造区尺寸是另一回事，它是场景规模，确实从配置读。
///
/// 放规则层而不是引擎层的理由与 <see cref="UIMetrics"/> 相同：死区与震幅用屏幕像素，两种视角
/// 共用同一个数、各自除以缩放得到世界像素，所以这些数必须能被侧视缩放整除 —— 除不尽就出现半
/// 像素，而半像素会破坏像素对齐（实测）。有测试盯着这些关系，改坏当场失败。
/// </remarks>
public static class CameraFeel
{
    /// <summary>
    /// 跟随死区的半宽，屏幕像素。角色在死区内移动时镜头不动。
    /// </summary>
    /// <remarks>
    /// 上限按「角色始终留在画面中间那一半」取，也就是不超过视野半宽的一半。再大就意味着角色能贴到
    /// 屏幕边缘而镜头还不动，那时玩家看不见自己要去的方向。下限按「别小到每帧都拖着镜头」取，
    /// 一个排版栅格。
    /// </remarks>
    public const int DeadzoneHalfWidthScreenPx = 48;

    /// <summary>跟随死区的半高，屏幕像素。竖向取得比横向小 —— 纵向视野本来就比横向窄。</summary>
    public const int DeadzoneHalfHeightScreenPx = 32;

    /// <summary>
    /// 屏幕震动的幅度，屏幕像素。必须能被侧视缩放整除，否则换算成世界像素会带半像素。
    /// </summary>
    /// <remarks>
    /// 震屏只给重击与特定事件，而且要能关掉。幅度上限按「别把角色晃出死区」取，不超过死区半高；
    /// 下限是一个世界像素乘缩放 —— 比这更小的位移在整数网格上表达不出来，取整后恒为 0。
    /// </remarks>
    public const int ShakeAmplitudeScreenPx = 4;

    /// <summary>震动时长，秒。</summary>
    /// <remarks>
    /// 上限跟着顿帧走：顿帧不能长到打断连段的输入节奏，震动超过四分之一秒就会盖住下一次输入的
    /// 反馈。下限是一个完整的震动周期，低于它玩家只看到一次跳动，不是震动。
    /// </remarks>
    public const double ShakeSeconds = 0.12;

    /// <summary>轻击命中的微震幅度，屏幕像素。</summary>
    /// <remarks>
    /// 轻击也要震，否则轻击打上去镜头毫无反应。顿帧得配同帧的视觉与声音才读得出力量，镜头是其中
    /// 一路，给轻击一点点震是把这一路补上，不是把它拉到重击那么重 —— 轻重靠幅度与时长区分，
    /// 不靠「有没有震」。
    ///
    /// 取 2：正好是 <see cref="ShakeAmplitudeScreenPx"/> 那条注释里说的下限，一个世界像素乘侧视
    /// 缩放。也就是说这是表达得出来的最小一抖，而重击是它的两倍。
    /// </remarks>
    public const int LightHitShakeAmplitudeScreenPx = 2;

    /// <summary>轻击微震的时长，秒。比重击短 —— 轻击连段要流畅，镜头不能挂在上一下。</summary>
    /// <remarks>
    /// 取 0.08：比 <see cref="ShakeSeconds"/> 短三分之一，同时还装得下一个完整的震动周期
    /// （<see cref="ShakeHertz"/> 是换向频率，一个完整周期要两次换向，约 0.067 秒）。不取更短是
    /// 因为装不下一个周期时玩家看到的是一次跳动而不是震动；真要改成「一下顿挫」得连那条下限一起重议。
    /// </remarks>
    public const double LightHitShakeSeconds = 0.08;

    /// <summary>一次命中该震多大、多久：重击用默认那一组，轻击用微震那一组。</summary>
    /// <remarks>
    /// 轻重到幅度与时长的映射只有这一处。训练房与打击反馈的调试场景都调它，所以两边不会各写一份
    /// 然后慢慢对不上 —— 调试场景与实际玩法悄悄测的是不同东西，是最难发现的那种错。
    /// </remarks>
    public static (int AmplitudeScreenPx, double Seconds) HitShake(bool heavy) =>
        heavy
            ? (ShakeAmplitudeScreenPx, ShakeSeconds)
            : (LightHitShakeAmplitudeScreenPx, LightHitShakeSeconds);

    /// <summary>震动换向频率，赫兹。不跟帧率绑 —— 帧率变化不该改变震动看起来的样子。</summary>
    public const int ShakeHertz = 30;

    /// <summary>边缘推镜的触发余量，单位是格。角色离可建造区边缘不足这么多格就开始推镜。</summary>
    /// <remarks>
    /// 用格而不是像素，因为它描述的是建造网格上的距离，玩家心里的单位也是格。上限受视野管着：
    /// 余量必须小于视野半宽，否则一进场就在推镜。
    /// </remarks>
    public const int EdgePushMarginCells = 3;

    /// <summary>边缘推镜的速度，世界像素每秒。比手动滚动慢 —— 它是提示而不是操作。</summary>
    public const int EdgePushPixelsPerSecond = 128;

    /// <summary>建造时手动滚动的速度，世界像素每秒。</summary>
    /// <remarks>
    /// 要满足的是「横穿可建造区不该久到让人不耐烦」。按 <c>data/config/game.json</c> 里当前的
    /// 可建造区尺寸算，这个速度横穿约 3.3 秒、纵穿 2.5 秒。画布尺寸改了这笔账要跟着重算，
    /// 否则注释里的秒数会静默过期。手感最终由作者实机调。
    /// </remarks>
    public const int ScrollPixelsPerSecond = 384;

    /// <summary>边缘推镜触发余量换算成世界像素。</summary>
    public static int EdgePushMarginPixels => EdgePushMarginCells * UIMetrics.BaseUnit;
}
