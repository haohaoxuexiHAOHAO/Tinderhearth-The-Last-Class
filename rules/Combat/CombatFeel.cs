namespace Tinderhearth.Rules.Combat;

/// <summary>战斗手感的那一批可调数：帧数、速度与距离。</summary>
/// <remarks>
/// 这些数不进设计仓 design/数值模型.md，也不进 data/config/game.json。前者持有的是能写成算式
/// 核对的平衡量，而前后摇、无敌窗、顿帧、击退只能实机逐帧调，写不出算式；后者是给 mod 和联机
/// 用的内容，手感一旦外置，别人就能把无敌帧改成 999，所有关卡都在未知手感下跑。
///
/// 量纲写在成员名里：时长用帧（物理步固定 60Hz，见 <see cref="PhysicsTicksPerSecond"/>），速度
/// 用世界像素每秒（引擎乘各自的 delta），距离用世界像素。用帧不用秒，是因为判定窗口与连段衔接
/// 本来就是逐帧的，换成秒会多一道取整。
///
/// 可调的手感量只放这一处，两台状态机都引这里的常量、不写字面帧数。于是调手感是改这一个文件，
/// 不是全代码翻找散落的数字。
/// </remarks>
public static class CombatFeel
{
    /// <summary>物理步频率，赫兹。帧与秒的换算基准，取 Godot 的默认物理帧率。</summary>
    public const int PhysicsTicksPerSecond = 60;

    // ── 移动与跳跃 ──────────────────────────────────────────────────────

    /// <summary>地面移动速度，世界像素／秒。</summary>
    /// <remarks>
    /// 还没实机调过。它是按「约 3 秒横穿一屏」定的，而那条算式用的视野只有 320px 宽；项目的
    /// 逻辑分辨率是 640 宽，照这个速度横穿要 6 秒多 —— 依据和值对不上，得重定一个。
    /// </remarks>
    public const int MoveSpeedPixelsPerSecond = 104;

    /// <summary>水平加速度，世界像素／秒²。</summary>
    /// <remarks>还没实机调过。按它算，从静止加到地面移动速度要 6 帧。</remarks>
    public const int HorizontalAccelerationPixelsPerSecondSquared = 1040;

    /// <summary>水平减速度，世界像素／秒²。</summary>
    /// <remarks>还没实机调过。按它算，从地面移动速度刹到停要 4 帧。</remarks>
    public const int HorizontalDecelerationPixelsPerSecondSquared = 1560;

    /// <summary>纵深移动速度，世界像素／秒。</summary>
    /// <remarks>
    /// 三条依据都能核：约为横向行走速度的 58%，与同类横版把纵深走得比横向慢的惯例一致；走过一排
    /// 纵深（<see cref="DepthBand.RowSpacingWorldPx"/>）要 16 帧约 0.27 秒，走完整条带 0.8 秒；
    /// 60Hz 下正好每帧 1 世界像素，于是纵深每帧都落在整像素上，绘制偏移不必取整、不会抖。
    ///
    /// 算式说明的是这个数有来处，不是手感对。快到躲不出决策、慢到挪不开都只能实机看。它和命中的
    /// 纵深容差是一对：容差一改，这个速度大概要跟着重调。
    /// </remarks>
    public const int DepthSpeedPixelsPerSecond = 60;

    /// <summary>跳跃初速，世界像素／秒，向上。</summary>
    public const int JumpInitialPixelsPerSecond = 260;

    /// <summary>重力，世界像素／秒²。</summary>
    /// <remarks>
    /// 与初速一起决定跳跃高度与滞空：约 0.27 秒到顶，逐帧积分算出的峰高约 37 世界像素，
    /// 空中连击有窗口但不飘。
    /// </remarks>
    public const int GravityPixelsPerSecondSquared = 980;

    // ── 闪避与冲刺 ──────────────────────────────────────────────────────

    /// <summary>闪避的水平速度，世界像素／秒。</summary>
    public const int DodgeSpeedPixelsPerSecond = 168;

    /// <summary>闪避的纵深速度，世界像素／秒。</summary>
    /// <remarks>
    /// 不复用 <see cref="DodgeSpeedPixelsPerSecond"/>：那个数在 18 帧里走 50.4px，比整条纵深带
    /// （<see cref="DepthBand.WidthWorldPx"/>）还长，于是每次纵深闪避都撞在带沿上 —— 落脚处由钳制
    /// 决定而不是由输入决定，「往里挪半步」和「翻到最里侧」变成同一个结果，而这件事不报错。
    ///
    /// 取 90：60Hz 下每帧 1.5px，18 帧走 27px，约 1.7 排，出了当前这一排又离带沿还有余量。它是
    /// 纵深行走速度的 1.5 倍，对应横向那边闪避约为行走的 1.6 倍。还没实机调过。
    /// </remarks>
    public const int DodgeDepthSpeedPixelsPerSecond = 90;

    /// <summary>闪避总时长，帧。闪步从起手到收尾的全长。</summary>
    public const int DodgeDurationFrames = 18;

    /// <summary>无敌窗起始帧，含，从闪避第 0 帧算起。起手头几帧是「甩出去」，还没无敌。</summary>
    public const int DodgeInvulnStartFrame = 2;

    /// <summary>无敌窗结束帧，不含。收尾那几帧无敌已过，此时被打到仍会中招 —— 闪步尾端有风险是有意的。</summary>
    public const int DodgeInvulnEndFrame = 13;

    /// <summary>冲刺速度，世界像素／秒。比闪避快但没有无敌帧 —— 它是位移手段，不是防御手段。</summary>
    public const int RunSpeedPixelsPerSecond = 176;

    // ── 轻攻击连段 ──────────────────────────────────────────────────────

    /// <summary>轻攻击每段的前摇帧数。</summary>
    public const int LightStartupFrames = 4;

    /// <summary>轻攻击每段的命中帧数。判定框只在这几帧开着。</summary>
    public const int LightActiveFrames = 3;

    /// <summary>轻攻击每段的后摇帧数。</summary>
    public const int LightRecoveryFrames = 8;

    /// <summary>轻攻击的连段衔接窗，帧。后摇最后这么多帧内再按轻攻击就取消剩余后摇、续下一段。</summary>
    /// <remarks>必须不大于后摇帧数。大过了，取消窗会覆盖整个后摇，后摇等于没有。</remarks>
    public const int LightComboWindowFrames = 6;

    /// <summary>轻攻击连段一共几段。</summary>
    public const int LightChainLength = 3;

    // ── 重攻击连段 ──────────────────────────────────────────────────────

    /// <summary>重攻击每段的前摇帧数。比轻攻击长 —— 重的代价是慢。</summary>
    public const int HeavyStartupFrames = 8;

    /// <summary>重攻击每段的命中帧数。</summary>
    public const int HeavyActiveFrames = 4;

    /// <summary>重攻击每段的后摇帧数。</summary>
    public const int HeavyRecoveryFrames = 14;

    /// <summary>重攻击的连段衔接窗，帧。后摇末尾这么多帧内按重攻击续下一段，必须不大于后摇帧数。</summary>
    public const int HeavyComboWindowFrames = 10;

    /// <summary>重攻击连段一共几段。取 1，也就是重击是单招、不连段；以后可能再加连段。</summary>
    public const int HeavyChainLength = 1;

    /// <summary>攻击输入的缓冲帧数。</summary>
    /// <remarks>
    /// 续段窗只在后摇末尾开着几帧，玩家狂点时按键很难正好落在窗内，实机表现是「一直第一段、
    /// 偶尔才连上第二段」。缓冲记住最近这么多帧内按下过的攻击键，续段窗一开就消费掉。
    ///
    /// 起手那一下不留缓冲（<see cref="ComboStateMachine"/> 的 <c>Begin</c> 清掉），所以单次点击
    /// 不会被缓冲误连到第二段。取 6，约一个轻击续段窗那么宽，够覆盖常见的狂点节奏。还没实机调过。
    /// </remarks>
    public const int InputBufferFrames = 6;

    /// <summary>轻击击退距离，世界像素。</summary>
    /// <remarks>
    /// 实机定为 0。轻击带击退会把敌人推出连段射程，于是只能平 A 一下就够不到了；归 0 让轻击
    /// 连段留在射程里。重击照旧击退。
    /// </remarks>
    public const int LightKnockbackWorldPx = 0;

    /// <summary>重击击退距离，世界像素。还没实测过。</summary>
    public const int HeavyKnockbackWorldPx = 16;

    /// <summary>轻击硬直帧数。还没实测过。</summary>
    public const int LightHitstunFrames = 10;

    /// <summary>重击硬直帧数。还没实测过。</summary>
    public const int HeavyHitstunFrames = 18;

    /// <summary>轻击顿帧帧数。还没实测过。</summary>
    public const int LightHitstopFrames = 3;

    /// <summary>重击顿帧帧数。还没实测过。</summary>
    public const int HeavyHitstopFrames = 5;

    /// <summary>木桩那种闪白持续几帧，按非冻结帧算。精灵角色走 <see cref="HitFlashFrames"/>。</summary>
    /// <remarks>
    /// 还没实机调过。木桩在自己的推进里递减它，顿帧期间不走，所以重击时屏幕上的可见时长是
    /// 顿帧帧数再加这个数。
    /// </remarks>
    public const int FlashFrames = 6;

    /// <summary>打击音的音高随机浮动幅度，百分比，上下各这么多。</summary>
    /// <remarks>
    /// 不抖的话，同一个采样每次都以同一音高响，连段打出去听起来是「机器在响」而不是「打了三下」。
    ///
    /// 取 8：能听出每下不同，又不至于把打击音抖成音阶。实机听过，占位采样这个阶段可以接受；采样
    /// 本身以后会换，换的时候这个数大概要重听一遍。正式音效的混音定下来后它可能整个搬走。
    /// </remarks>
    public const int AudioPitchJitterPercent = 8;

    /// <summary>命中当帧那一下极短白闪持续几帧，按真实帧算，不受顿帧冻结影响。</summary>
    /// <remarks>
    /// 和 <see cref="FlashFrames"/> 刻意不复用：那个按非冻结帧递减，重击时可见时长是顿帧再加它，
    /// 十来帧全白会晃眼。这一条要的是相反的东西 —— 命中那一下爆一下就没。
    ///
    /// 按真实帧而不是非冻结帧计时：按非冻结帧算的话，整个顿帧期间它都维持全白，两帧会被顿帧长度
    /// 悄悄放大成四到七帧，调顿帧就连带改了白闪的观感。按真实帧算，两者互不影响。
    ///
    /// 取 2，60Hz 下约 33 毫秒。取 1 帧的话一次掉帧就整个看不见，而这一下正是要让眼睛看到的。
    /// 作者在训练房确认过这一版，但那时场上还没有打击音，顿帧与震屏也还没按同一个爆点协同过。
    /// </remarks>
    public const int HitFlashFrames = 2;

    // ── 代码影子 ────────────────────────────────────────────────────────
    //
    // 影子由代码画，不进精灵帧。它要同时说明两件事：角色站在纵深的哪一排，以及此刻离地多高 ——
    // 带纵深之后这两件事在屏幕上都表现为上下移动，没有影子就分不开「往里走了」和「跳起来了」。
    // 所以它关系到画面读不读得懂，不只是装饰。
    //
    // 随高度只缩小、不变淡，理由见 DepthRendering.ShadowScaleAt：变淡要用 alpha，而项目把
    // 「只用完全透明或完全不透明」定成了绝对规则。

    /// <summary>影子宽度占角色本体宽度的百分比。还没实机调过。</summary>
    /// <remarks>
    /// 影子宽度跟着当前那一帧的本体宽度走，不然攻击与闪避时影子一动不动，实机看上去很怪。取 90：
    /// 站着时本体 19px、影子 17px，比脚略窄、不从脚边露出来；拳伸到最远时本体 29px，影子跟着拉长。
    ///
    /// 逐帧取，不按动作取最大值。后者（同一动作内影子恒定，为躲开待机呼吸那 1px 抖动）被实测否掉：
    /// 轻击整段的影子都是伸出后的 26px，而前摇帧身体只有 19px，影子宽出一截、看起来像偏了。反过来，
    /// 待机呼吸让影子轻微变化其实是对的，真影子就该跟着身体变。
    ///
    /// 只变宽度，不变形状、不偏中心。形状仍是椭圆，中心仍在脚底。压扁精灵做真投影会跟得更紧，但
    /// 那要非整数缩放，撞上项目的整数缩放与最近邻过滤。作者只要保留基本的阴影细节，宽度这一维够了。
    /// </remarks>
    public const int ShadowWidthPercentOfBody = 90;

    /// <summary>影子贴地时的高度，也就是它在纵深方向上的厚度，世界像素。还没实机调过。</summary>
    /// <remarks>
    /// 取 6：约为影子宽度的三分之一，是「地面上一个被压扁的圆」该有的比例；同时明显小于一排纵深
    /// （<see cref="DepthBand.RowSpacingWorldPx"/>），于是相邻两排的影子不会连成一片。
    /// </remarks>
    public const int ShadowHeightWorldPx = 6;

    /// <summary>影子缩到最小时占原尺寸的百分比。还没实机调过。</summary>
    public const int ShadowMinScalePercent = 50;

    /// <summary>影子缩到最小所需的离地高度，世界像素。还没实机调过。</summary>
    /// <remarks>
    /// 取成约等于跳跃峰高，这样一次完整跳跃正好把影子从原尺寸缩到最小，高度提示用满整个行程：
    /// 取得比峰高大，影子在空中几乎不变；取得比峰高小，上升段走一半之后就没有提示了。
    ///
    /// 「约等于」是实话 —— 按初速与重力逐帧积分，峰高是 37px 左右，这里取的是 32。
    /// </remarks>
    public const int ShadowShrinkHeightWorldPx = 32;

    // ── 判定框 ──────────────────────────────────────────────────────────
    //
    // 按段分开取值。轻击三段各有独立动画（light、light2、light3），三段打出去的拳脚伸到的距离与
    // 高度都不一样，尤其第三段是踢腿，伸得比前两段的直拳都远、也更低。三段共用一个框的话，画面上
    // 那一脚踢到脚边、判定框却停在拳的位置，玩家读到的是「明明够到了却没打中」，而这件事不报错。
    //
    // 下面每个数是从精灵表量出来的：取「伸出静止起手姿之外的那部分」（也就是打出去的那只拳脚）的
    // 包围盒，按判定窗口那两帧逐段取极值 —— 宽是窗内两帧的最大右伸，高是两帧行区间的并，中心是
    // 脚底之上到那个区间中点的高度。那个窗口是哪两帧由引擎层的精灵表映射定，见 PlayerActor.cs
    // 「攻击的三个阶段各用哪几张图」那段注释；本层读不到它，所以这是一条口头对齐、不是代码依赖。
    //
    // 量出来的是「跟画面对齐」，不是「手感对」。素材、判定窗口与这里的常量这三处原先有自动核对，
    // 现在没有了，改一处不会再被当场拦下 —— 改素材或改判定窗口时，请在 Godot 里开训练房的判定框
    // 叠层（按 V）看一眼框还贴不贴那只拳脚。

    /// <summary>轻击第 1 段（直拳）判定框宽度，也就是伸展距离，世界像素。判定帧量出来的，还没实机调过。</summary>
    public const int Light1HitboxWidthWorldPx = 15;

    /// <summary>轻击第 1 段判定框高度，世界像素。判定帧量出来的，还没实机调过。</summary>
    public const int Light1HitboxHeightWorldPx = 4;

    /// <summary>轻击第 1 段判定框中心离脚底的高度，世界像素。量出来 17.5、取整 18，还没实机调过。</summary>
    public const int Light1HitboxCenterYWorldPx = 18;

    /// <summary>轻击第 2 段判定框宽度，也就是伸展距离，世界像素。判定帧量出来的，还没实机调过。</summary>
    public const int Light2HitboxWidthWorldPx = 12;

    /// <summary>轻击第 2 段判定框高度，世界像素。判定帧量出来的，还没实机调过。</summary>
    public const int Light2HitboxHeightWorldPx = 3;

    /// <summary>轻击第 2 段判定框中心离脚底的高度，世界像素。量出来 17，还没实机调过。</summary>
    public const int Light2HitboxCenterYWorldPx = 17;

    /// <summary>轻击第 3 段（踢腿，伸得更远更低）判定框宽度，世界像素。判定帧量出来的，还没实机调过。</summary>
    public const int Light3HitboxWidthWorldPx = 16;

    /// <summary>轻击第 3 段判定框高度，世界像素。判定帧量出来的，还没实机调过。</summary>
    public const int Light3HitboxHeightWorldPx = 6;

    /// <summary>轻击第 3 段判定框中心离脚底的高度，世界像素。量出来 15.5、取整 16，还没实机调过。</summary>
    public const int Light3HitboxCenterYWorldPx = 16;

    /// <summary>重击判定框宽度，也就是伸展距离，世界像素。判定帧量出来的，还没实机调过。</summary>
    public const int HeavyHitboxWidthWorldPx = 21;

    /// <summary>重击判定框高度，世界像素。判定帧量出来的，还没实机调过。</summary>
    public const int HeavyHitboxHeightWorldPx = 16;

    /// <summary>重击判定框中心离脚底的高度，世界像素。量出来 17.5、取整 18，还没实机调过。</summary>
    public const int HeavyHitboxCenterYWorldPx = 18;

    /// <summary>命中的纵深容差，世界像素。双方纵深差不超过它才算打得到。</summary>
    /// <remarks>
    /// 取相邻两排纵深间距（<see cref="DepthBand.RowSpacingWorldPx"/>）的一半：看上去重叠的两具身体
    /// 打得到，隔一排打不到、且离容差有整整一倍余量，不会在浮点末位上摇摆。纵深是连续的不是轨道，
    /// 同一排上两个人各偏半排是常态，取半排刚好把「都算在这一排」盖满。
    ///
    /// 排距与带宽是怎么算出来的见设计仓 canon/gameplay/战斗与关卡.md 的「可行走纵深是怎么算出来的」
    /// 一节。那一节现在算出来的排距与带宽跟代码里这几个数对不上，要重新对一遍。
    ///
    /// 算式说明的是这个数有来处，不是手感对。容差太小，玩家对着敌人挥却一直空，他不会归因于站位、
    /// 只会觉得判定不准；太大则纵深挪步失去意义。它和 <see cref="DepthSpeedPixelsPerSecond"/> 是
    /// 一对，改一个另一个大概要跟着重调。
    /// </remarks>
    public const int HitDepthToleranceWorldPx = 8;

    /// <summary>实体阻挡的纵深阈值，世界像素。双方纵深差不超过它才互相挡住。</summary>
    /// <remarks>
    /// 和 <see cref="HitDepthToleranceWorldPx"/> 刻意是两个常量：一个是「打得着」的宽容量，一个是
    /// 「占同一格」的物理量。两者此刻都等于 8 是巧合，不是等式 —— 它们会各自单独改，共用一个常量
    /// 会让改一个静默改掉另一个。
    ///
    /// 同样取相邻两排间距（<see cref="DepthBand.RowSpacingWorldPx"/>）的一半：看着重叠的两具身体
    /// 互相挡住，隔一排不挡。隔一排那一头更要紧 —— 不该挡的挡了，玩家看到的是两个明显不在一排的
    /// 东西卡在一起，他读不出原因。
    ///
    /// 不拿影子的纵深厚度（<see cref="ShadowHeightWorldPx"/>）当本体占地：那个数是按长相定的，拿
    /// 它当阻挡阈值会让改影子长相就改碰撞行为。单测钉着一条关系：本阈值不大于命中容差，反过来会
    /// 出现「被挡住却打不着」。阈值是一个数、不按物件分，巨石那类要分开得先有真实的关卡物件。
    /// </remarks>
    public const int BlockDepthThresholdWorldPx = 8;

    // ── 派生 ────────────────────────────────────────────────────────────

    /// <summary>一个物理帧的时长，秒。速度乘它得到每帧位移。</summary>
    public static double FrameSeconds => 1.0 / PhysicsTicksPerSecond;
}
