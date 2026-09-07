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

    /// <summary>跳跃初速，世界像素／秒（向上）。</summary>
    public const int JumpInitialPixelsPerSecond = 260;

    /// <summary>重力，世界像素／秒²。</summary>
    /// <remarks>与初速一起决定跳跃高度与滞空：约 0.27 秒到顶、峰高约一个精灵格（32px），空中连击有窗口但不飘。</remarks>
    public const int GravityPixelsPerSecondSquared = 980;

    // ── 闪避与冲刺 ──────────────────────────────────────────────────────

    /// <summary>闪避的水平速度，世界像素／秒。</summary>
    public const int DodgeSpeedPixelsPerSecond = 168;

    /// <summary>闪避总时长，帧。翻滚从起到收的全长。</summary>
    public const int DodgeDurationFrames = 18;

    /// <summary>无敌窗起始帧（含），从闪避第 0 帧算。起手有几帧是「甩出去」还没无敌。</summary>
    public const int DodgeInvulnStartFrame = 2;

    /// <summary>无敌窗结束帧（不含）。收尾几帧无敌已过，此时被打到仍会中招 —— 翻滚尾端有风险是有意的。</summary>
    public const int DodgeInvulnEndFrame = 13;

    /// <summary>冲刺速度，世界像素／秒。比闪避快、无无敌帧，是位移不是防御。</summary>
    public const int DashSpeedPixelsPerSecond = 176;

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

    /// <summary>重攻击连段的段数。</summary>
    public const int HeavyChainLength = 2;

    // ── 派生 ────────────────────────────────────────────────────────────

    /// <summary>一个物理帧的时长，秒。速度乘它得到每帧位移。</summary>
    public static double FrameSeconds => 1.0 / PhysicsTicksPerSecond;
}
