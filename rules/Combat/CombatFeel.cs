namespace Tinderhearth.Rules.Combat;

/// <summary>
/// 战斗手感的**初值**（`GP-10`）。**这些数还没实机收敛过，归 `GP-6`。**
/// </summary>
/// <remarks>
/// 为什么这些数在这里，理由与 <c>CameraFeel</c> 同源：
///
/// **不进 `design/数值模型.md`。** 那份持有能进不等式判据、能被 <c>simulate_week.py</c> 重算的
/// 平衡量。手感帧数（前后摇、无敌窗、顿帧、击退量）一条公式都不进 —— 正典明确把它们排出 `GP-2`，
/// 因为它们只能实机逐帧调，靠感觉调违反「结论必须有依据」。它们是 `GP-6` 的实测收敛对象。
///
/// **不进 `data/config/game.json`。** 那里放 mod 与联机都会撞的结构性容量。手感是表现规则不是
/// 内容，正典的「内容外置，规则不外置」把它划在外面：外置意味着 mod 能把无敌帧改成 999，那会
/// 让所有关卡在未知手感下运行。
///
/// **量纲一律写在成员名里。** 时长用**帧**（固定 60Hz 物理步，见 <see cref="PhysicsTicksPerSecond"/>），
/// 速度用**世界像素／秒**（引擎乘各自的 delta），距离用**世界像素**。帧而不是秒，是因为判定窗口
/// 与连段衔接都是逐帧的，秒会引入取整歧义。
///
/// **本文件是全部可调手感量的唯一落点（`FR-18`）。** 两台状态机只引用这里的常量，不写字面帧数 ——
/// 于是调手感是改这一处，不是全代码翻找魔法数。
/// </remarks>
public static class CombatFeel
{
    /// <summary>物理步频率，赫兹。帧与秒的换算基准，Godot 默认物理帧率。</summary>
    public const int PhysicsTicksPerSecond = 60;

    // ── 移动与跳跃 ──────────────────────────────────────────────────────

    /// <summary>地面移动速度，世界像素／秒。</summary>
    /// <remarks>侧视有效视野宽 320 世界像素，主角 32px 宽；这个速度约 3 秒横穿全屏，够快又不失控。</remarks>
    public const int MoveSpeedPixelsPerSecond = 104;

    /// <summary>水平加速度，世界像素／秒²；未校准初值，104px/s 从静止用6帧达到。</summary>
    public const int HorizontalAccelerationPixelsPerSecondSquared = 1040;

    /// <summary>水平减速度，世界像素／秒²；未校准初值，104px/s 用4帧停止。</summary>
    public const int HorizontalDecelerationPixelsPerSecondSquared = 1560;

    /// <summary>纵深移动速度，世界像素／秒；`GP-15` 未校准初值，归 `GP-6`。</summary>
    /// <remarks>
    /// 三条依据，都可核：约为横向行走速度（104）的 58%，与 belt-scroll 那一类作品把纵深走得比
    /// 横向慢的惯例一致；一排纵深（<see cref="DepthBand.RowSpacingWorldPx"/> ＝16px）要走 16 帧
    /// 约 0.27 秒，走完整条 48px 带 0.8 秒；60Hz 下正好 1 世界像素／帧，于是纵深每帧都落在整
    /// 像素上，绘制偏移（`ENG-15`）不必每帧取整、不会抖。
    ///
    /// **这三条证明的是「有依据」，不是「手感对」。** 快到躲不出决策、慢到挪不开都只能实机看，
    /// 归 `GP-6`。纵深挪步与命中容差（`GP-16`）是一对：容差一改，这个速度大概要跟着重调。
    /// </remarks>
    public const int DepthSpeedPixelsPerSecond = 60;

    /// <summary>跳跃初速，世界像素／秒（向上）。</summary>
    public const int JumpInitialPixelsPerSecond = 260;

    /// <summary>重力，世界像素／秒²。</summary>
    /// <remarks>与初速一起决定跳跃高度与滞空：约 0.27 秒到顶、峰高约一个精灵格（32px），空中连击有窗口但不飘。</remarks>
    public const int GravityPixelsPerSecondSquared = 980;

    // ── 闪避与冲刺 ──────────────────────────────────────────────────────

    /// <summary>闪避的水平速度，世界像素／秒。</summary>
    public const int DodgeSpeedPixelsPerSecond = 168;

    /// <summary>闪避的纵深速度，世界像素／秒；`GP-15` 未校准初值，归 `GP-6`。</summary>
    /// <remarks>
    /// **为什么纵深闪避不复用 <see cref="DodgeSpeedPixelsPerSecond"/>：** 那个 168 是按 320px 宽的
    /// 横向视野定的，18 帧走 50.4px。同一个数放到 48px 宽的纵深带上，一次纵深闪避的位移就超过
    /// 整条带 —— 于是每次纵深闪避都会撞在带沿上，**落点由钳制决定而不是由输入决定**，「往里挪
    /// 半步」与「翻到最里侧」变成同一个结果。这件事不报错，只表现为「纵深闪避没有分寸」。
    ///
    /// 取 90：60Hz 下 1.5 世界像素／帧，18 帧走 27px，约 1.7 排（16px／排），出了当前这一排又
    /// 离带沿还有余量。它是纵深行走速度的 1.5 倍，对应横向那边闪避约为行走 1.6 倍的比例。
    /// </remarks>
    public const int DodgeDepthSpeedPixelsPerSecond = 90;

    /// <summary>闪避总时长，帧。闪步从起到收的全长。</summary>
    public const int DodgeDurationFrames = 18;

    /// <summary>无敌窗起始帧（含），从闪避第 0 帧算。起手有几帧是「甩出去」还没无敌。</summary>
    public const int DodgeInvulnStartFrame = 2;

    /// <summary>无敌窗结束帧（不含）。收尾几帧无敌已过，此时被打到仍会中招 —— 闪步尾端有风险是有意的。</summary>
    public const int DodgeInvulnEndFrame = 13;

    /// <summary>冲刺速度，世界像素／秒。比闪避快、无无敌帧，是位移不是防御。</summary>
    public const int RunSpeedPixelsPerSecond = 176;

    // ── 轻攻击连段 ──────────────────────────────────────────────────────

    /// <summary>轻攻击每段的前摇帧。</summary>
    public const int LightStartupFrames = 4;

    /// <summary>轻攻击每段的命中（Active）帧。判定框只在这几帧启用。</summary>
    public const int LightActiveFrames = 3;

    /// <summary>轻攻击每段的后摇帧。</summary>
    public const int LightRecoveryFrames = 8;

    /// <summary>轻攻击的连段衔接窗，帧。后摇的**最后**这么多帧内再按轻攻击就取消剩余后摇、续下一段。必须 ≤ 后摇。</summary>
    public const int LightComboWindowFrames = 6;

    /// <summary>轻攻击连段的段数。</summary>
    public const int LightChainLength = 3;

    // ── 重攻击连段 ──────────────────────────────────────────────────────

    /// <summary>重攻击每段的前摇帧。比轻攻击长 —— 重的代价是慢。</summary>
    public const int HeavyStartupFrames = 8;

    /// <summary>重攻击每段的命中帧。</summary>
    public const int HeavyActiveFrames = 4;

    /// <summary>重攻击每段的后摇帧。</summary>
    public const int HeavyRecoveryFrames = 14;

    /// <summary>重攻击的连段衔接窗，帧。后摇末尾这么多帧内按重攻击续下一段。必须 ≤ 后摇。</summary>
    public const int HeavyComboWindowFrames = 10;

    /// <summary>重攻击连段的段数。**作者 2026-09-11 定为 1：重击当前是单招、不连段**，后续可能再加连段。</summary>
    public const int HeavyChainLength = 1;

    /// <summary>攻击输入缓冲帧（`GP-14` 阶段 1 实机补，未校准初值，归 `GP-6`）。</summary>
    /// <remarks>
    /// 续段窗只在后摇末尾开着 <see cref="LightComboWindowFrames"/> 帧，玩家狂点时按键很难正好落在
    /// 窗内 —— 没有缓冲就表现为「一直第一段、偶尔才连上第二段」（`GP-14` 阶段 1 实机发现）。缓冲
    /// 记住最近这么多帧内的攻击边沿，续段窗一开就消费；起手那一下不留缓冲（<see cref="ComboStateMachine"/>
    /// 的 <c>Begin</c> 清掉），所以单次点击不会被缓冲误连到第二段。取 6：约一个续段窗宽，够覆盖常见
    /// 狂点节奏，实机收敛归 `GP-6`。
    /// </remarks>
    public const int InputBufferFrames = 6;

    /// <summary>轻击击退距离，世界像素。**作者 2026-09-11 实机定为 0**：轻击有击退会把敌人推出连段射程、只能平 A 一下就够不到，归 0 让轻击连段留在射程里。重击照旧击退。</summary>
    public const int LightKnockbackWorldPx = 0;

    /// <summary>重击击退距离，世界像素；未实测初值。</summary>
    public const int HeavyKnockbackWorldPx = 16;

    /// <summary>轻击硬直帧；未实测初值。</summary>
    public const int LightHitstunFrames = 10;

    /// <summary>重击硬直帧；未实测初值。</summary>
    public const int HeavyHitstunFrames = 18;

    /// <summary>轻击顿帧；未实测初值。</summary>
    public const int LightHitstopFrames = 3;

    /// <summary>重击顿帧；未实测初值。</summary>
    public const int HeavyHitstopFrames = 5;

    /// <summary>闪白持续的非冻结帧数，GP-13 未校准初值。**木桩几何专用**，精灵角色走 <see cref="HitFlashFrames"/>。</summary>
    public const int FlashFrames = 6;

    /// <summary>打击音的音高随机浮动幅度，百分之几（上下各这么多）；`GP-20` 未校准初值。</summary>
    /// <remarks>
    /// 为什么要抖：同一个采样每次都以同一音高响，连段打出去就是「机器在响」而不是「打了三下」。
    /// 借件工程也是这么做的（它的 sound_manager 对每次播放随机音高）。
    ///
    /// 取 8（±8%）：能听出每下不同，又不会把打击音抖成音阶。**作者 2026-09-13 实机听过这一版，
    /// 占位阶段可接受**；采样本身后续会替换，届时这个数大概要跟着重听一遍。
    ///
    /// **正式音效的采样率、总线与混音归
    /// `ART-1`**，那时这个数可能整个搬走；放这里是为了守住「手感可调量只有一处」（`FR-18`），
    /// 不让它变成散落在引擎层的魔法数。
    /// </remarks>
    public const int AudioPitchJitterPercent = 8;

    /// <summary>命中当帧的极短白闪时长，**真实帧**（不受顿帧冻结）；`GP-20`，**作者 2026-09-13 实机确认为 2**。</summary>
    /// <remarks>
    /// **与 <see cref="FlashFrames"/> 是两个数，刻意不复用。** 那个 6 是木桩的，且按**非冻结帧**递减
    /// （<c>TrainingDummy</c> 在自己的推进里减），于是重击时实际可见时长是「顿帧 5 帧 ＋ 6 帧」——
    /// 十来帧全白，正是作者说的「晃眼」，也是 `GP-14` 把靶从木桩换成真角色时顺手甩掉的东西。本条要的
    /// 是相反的东西：命中那一下**爆一下就没**。
    ///
    /// **按真实帧计时，不按非冻结帧**，口径同 <c>HitSpark</c>（那个也刻意不进顿帧 gate）。理由是两条：
    /// 白闪按非冻结帧算的话，整个顿帧期间它都维持全白，「1–2 帧」会被顿帧长度悄悄放大成 4–7 帧，调
    /// 顿帧就会连带改变白闪观感；按真实帧算则两者解耦，顿帧怎么调白闪都是这么长。
    ///
    /// 取 2：60Hz 下约 33ms，业界那条「命中给受方 1–2 帧白闪」的下限取上沿 —— 取 1 帧的话一次掉帧
    /// 就整个看不见，而这一下正是要让眼睛看到的。**「几帧才不晃眼」只能实机看**，作者 2026-09-13
    /// 在训练房实机确认这一版可以，所以它不再是未校准初值。
    ///
    /// **但它没有被单独隔离验证过**：确认那一刻场上还没有打击音，顿帧与震屏也还没按「爆点」协同过
    /// （`GP-20` 后两步）。四件事叠在同一帧的观感会互相影响，届时若要重调走 `GP-6`，并写清新依据 ——
    /// 今天这条确认只覆盖「白闪 ＋ 已有的顿帧/震屏/火花」这个组合。
    /// </remarks>
    public const int HitFlashFrames = 2;

    // ── 代码影子（`ENG-15` 未校准初值，归 `GP-6`）──────────────────────
    //
    // 影子由代码画、不进精灵帧（`ART-6` 定的口径）。它承担两件事：告诉玩家角色**站在纵深的
    // 哪一排**，以及**此刻离地多高** —— 带纵深之后这两件事在屏幕上都是「上下移动」，没有影子
    // 就分不开「往里走了」和「跳起来了」。所以它是可读性的承重件，不是装饰。
    //
    // **随高度只缩小、不变淡**，理由见 `DepthRendering.ShadowScaleAt`：变淡要 alpha，而项目
    // 把「只用完全透明或完全不透明」定为绝对规则。

    /// <summary>影子宽度占角色本体宽度的百分比；未校准初值。</summary>
    /// <remarks>
    /// **影子宽度跟着当前那一帧的本体宽度走**（作者 2026-09-09 实机反馈：攻击与闪避时影子一动
    /// 不动「看上去很奇怪」）。取 90：站着时本体 19px，影子 17px，比脚略窄、不从脚边露出来；
    /// 拳伸到最远时本体 29px，影子跟着拉长。
    ///
    /// **逐帧取，不按动作取最大值。** 先试过后者（同一动作内影子恒定，理由是躲开待机呼吸带来的
    /// 1px 抖动），**实测截图否掉了它**：轻击整段的影子都是伸出后的 26px，而前摇帧身体只有 19px，
    /// 于是影子比身体宽出一截、看起来像影子偏了 —— 那比「影子不变」更像缺陷。反过来，待机呼吸让
    /// 影子轻微变化其实是**对的**表现，真影子就该跟着身体变。
    ///
    /// **只变宽度，不变形状、不偏中心。** 形状仍是椭圆，中心仍在脚底。压扁精灵做真投影会跟得更
    /// 紧，但那要非整数缩放，撞 `UI-3`／`ENG-13` 的整数缩放与最近邻过滤。作者的口径是「保留基本
    /// 的阴影细节就好，不需要太细」，宽度这一维在那个口径内。
    /// </remarks>
    public const int ShadowWidthPercentOfBody = 90;

    /// <summary>影子贴地时的高度（纵深方向的厚度），世界像素；未校准初值。</summary>
    /// <remarks>
    /// 取 6：约为宽度的三分之一，是「地面上一个被压扁的圆」该有的比例；同时明显小于一排纵深
    /// （<see cref="DepthBand.RowSpacingWorldPx"/> ＝16px），于是相邻两排的影子不会连成一片。
    /// </remarks>
    public const int ShadowHeightWorldPx = 6;

    /// <summary>影子缩到最小时占原尺寸的百分比；未校准初值。</summary>
    public const int ShadowMinScalePercent = 50;

    /// <summary>影子缩到最小所需的离地高度，世界像素；未校准初值。</summary>
    /// <remarks>
    /// 取 32，即跳跃峰高（初速 260 与重力 980 算出约一个精灵格）。这样一次完整跳跃正好把影子
    /// 从原尺寸缩到最小，高度提示用满整个行程 —— 取得比峰高大，影子在空中几乎不变；取得比峰高
    /// 小，上升段一半之后就没有提示了。
    /// </remarks>
    public const int ShadowShrinkHeightWorldPx = 32;

    // ── 判定框（`ART-6` 实测导出，`GP-6` 待校准）──────────────────────────
    //
    // **按段分开取值**（作者 2026-09-11 定）。轻击三段各有独立动画（`light`／`light2`／`light3`），
    // 三段打出去的拳脚伸到的距离与高度都不一样 —— 尤其第 3 段是踢腿，伸得比前两段的直拳都远、
    // 且更低。若三段共用一个框（更早一版轻重共用 28×28 也是同一个病），画面上那一脚踢到脚边、
    // 判定框却停在拳的位置，玩家读到的是「这一下明明够到了却没打中」，且这件事**不报错**。重击
    // 仍是单招、自成一段。
    //
    // 下面每个数不是估的，是量出来的：`tools/import_role_sheets.py` 逐帧算出「伸出静止起手姿之外
    // 的那部分」（也就是打出去的那只拳脚）的包围盒，写进 `tools/asset-registry.json` 的 `逐帧伸展`；
    // `tools/check_assets.py` 的 `check_hitbox_binding` 再按 Active 窗口
    // （<c>PlayerActor.AttackActiveFirstFrame</c> 与 <c>AttackActiveSpan</c>）**逐段**取极值，与
    // 这里逐条比对：宽＝Active 两帧的最大右伸，高＝Active 两帧行区间的并，中心＝脚底之上到那个
    // 区间中点的高度。所以三处任意一处变了都会被当场拦下：改素材、改 Active 窗口、改常量。
    //
    // **仍是未校准初值，归 `GP-6`。** 量出来的是「跟画面对齐」，不是「手感对」—— 尤其轻击框只有
    // 三四像素高（作者画的直拳本身就这么高），实机若发现容易打空，那是 `GP-6` 要调的宽容量，不是
    // 回头改这条对齐口径。中心离脚底的高度也按段（原先轻重共用一个 18）：直拳落在脚底上方约 17px，
    // 踢腿更低（实测 15.5、取整 16）—— 再共用一个 18 会让踢腿的判定框浮在腿上方，那正是本轮要
    // 消除的「画面与判定对不上」，`check_hitbox_binding` 逐段核中心就会当场拦下。

    /// <summary>轻击第 1 段（直拳）判定框宽度（伸展距离），世界像素；Active 帧实测导出，未校准初值。</summary>
    public const int Light1HitboxWidthWorldPx = 15;

    /// <summary>轻击第 1 段判定框高度，世界像素；Active 帧实测导出，未校准初值。</summary>
    public const int Light1HitboxHeightWorldPx = 4;

    /// <summary>轻击第 1 段判定框中心离脚底的高度，世界像素；实测 17.5、取整 18，未校准初值。</summary>
    public const int Light1HitboxCenterYWorldPx = 18;

    /// <summary>轻击第 2 段判定框宽度（伸展距离），世界像素；Active 帧实测导出，未校准初值。</summary>
    public const int Light2HitboxWidthWorldPx = 12;

    /// <summary>轻击第 2 段判定框高度，世界像素；Active 帧实测导出，未校准初值。</summary>
    public const int Light2HitboxHeightWorldPx = 3;

    /// <summary>轻击第 2 段判定框中心离脚底的高度，世界像素；实测 17，未校准初值。</summary>
    public const int Light2HitboxCenterYWorldPx = 17;

    /// <summary>轻击第 3 段（踢腿，伸得更远更低）判定框宽度，世界像素；Active 帧实测导出，未校准初值。</summary>
    public const int Light3HitboxWidthWorldPx = 16;

    /// <summary>轻击第 3 段判定框高度，世界像素；Active 帧实测导出，未校准初值。</summary>
    public const int Light3HitboxHeightWorldPx = 6;

    /// <summary>轻击第 3 段判定框中心离脚底的高度，世界像素；实测 15.5、取整 16，未校准初值。</summary>
    public const int Light3HitboxCenterYWorldPx = 16;

    /// <summary>重击判定框宽度（伸展距离），世界像素；Active 帧实测导出，未校准初值。</summary>
    public const int HeavyHitboxWidthWorldPx = 21;

    /// <summary>重击判定框高度，世界像素；Active 帧实测导出，未校准初值。</summary>
    public const int HeavyHitboxHeightWorldPx = 16;

    /// <summary>重击判定框中心离脚底的高度，世界像素；实测 17.5、取整 18，未校准初值。</summary>
    public const int HeavyHitboxCenterYWorldPx = 18;

    /// <summary>命中的纵深容差，世界像素；`GP-16` 未校准初值，归 `GP-6`。</summary>
    /// <remarks>
    /// **只有这一个数是可调的**，「横向与纵深两个条件都要满足」不是（正典把它定为空间模型的直接
    /// 推论，见 <see cref="DepthOverlap"/>）。
    ///
    /// 取 8 是算出来的，不是凭手感给的。依据是正典那笔几何账里的两个可读性结论
    /// （`canon/gameplay/战斗与关卡.md` 的「48px 是怎么算出来的」）：**纵深差 8px 以下基本重叠**，
    /// **相邻两排差 16px 时 30px 高的精灵仍读得出谁在前**。于是把容差取成半排：
    ///
    /// - 看上去重叠的两具身体（差 ≤ 8px）打得到 —— 玩家看到的是「贴在一起」，判定就该同意；
    /// - 隔一排（差 16px ＝ <see cref="DepthBand.RowSpacingWorldPx"/>）打不到，且离容差有整整一倍
    ///   的余量，不会在浮点末位上摇摆；
    /// - 纵深连续、不是轨道，所以同一排上的两个人各偏半排是常态，最坏情况纵深差正好一排的一半 ——
    ///   容差取半排刚好把「都算在这一排」覆盖满。
    ///
    /// 判据合起来是一句能验的话：**同一排打得到、隔一排打不到**，两头都由单测与探针钉着。
    ///
    /// **算式证明的是「有依据」，不是「手感对」。** 正典点名这条与顿帧同级、两个方向都会坏：容差
    /// 太小，玩家对着敌人挥却一直空，他不会归因于站位、只会觉得判定不准；容差太大，纵深挪步失去
    /// 意义，48px 的排位是白做的。哪边更难受只能实机看，归 `GP-6`。它与
    /// <see cref="DepthSpeedPixelsPerSecond"/> 是一对：容差一改，纵深挪步速度大概要跟着重调。
    ///
    /// **`GP-17` 的实体阻挡阈值不用这个数**，那是另一个常量：这里是「打得着」的宽容量，那里是
    /// 「占同一格」的物理量。
    /// </remarks>
    public const int HitDepthToleranceWorldPx = 8;

    // 判定框中心离脚底的高度**已按段拆进上面的 `Light1/2/3` 与 `Heavy` 常量**（原先是共用的一个
    // `HitboxCenterYWorldPx`＝18）：直拳约 17.5、踢腿约 15.5，共用一个数会让踢腿的判定框浮在腿上方。
    // 它原先是 `Hitbox.cs` 里的字面量 −18，搬来这里是为守住本文件「全部可调手感量的唯一落点」（`FR-18`）。

    // ── 派生 ────────────────────────────────────────────────────────────

    /// <summary>一个物理帧的时长，秒。速度乘它得到每帧位移。</summary>
    public static double FrameSeconds => 1.0 / PhysicsTicksPerSecond;
}
