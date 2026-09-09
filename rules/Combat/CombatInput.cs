namespace Tinderhearth.Rules.Combat;

/// <summary>
/// 一个物理帧的战斗输入快照（`GP-10`，`GP-15` 加纵深轴）。
/// </summary>
/// <remarks>
/// 规则层不认识 Godot 的 <c>InputRouter</c>：引擎层每帧把玩家（或 A2 的 AI）意图压成这个结构
/// 再喂进来，于是「玩家 ≠ 主角」在类型上成立 —— 换成 AI 只是换一个 <see cref="CombatInput"/>
/// 的来源，两台状态机不必知道。
///
/// **边沿与持续态分开**：攻击、跳跃、闪避取「本帧刚按下」（边沿），冲刺取「按住」（持续）。
/// 冲刺是修饰键式的持续位移，正典要求它显式（按住才冲），其余三者是一次触发。
///
/// **移动是两个独立的轴，不是一个二维量**（`GP-15`）：<see cref="HorizontalSign"/> 走横向，
/// <see cref="DepthSign"/> 走纵深（正典把战斗关卡定为带连续可行走纵深的横版）。原来那个字段叫
/// <c>MoveSign</c>，加了纵深之后这个名字会读成「移动轴」而纵深也是移动 —— 于是「把纵深接到
/// 横向字段上」变成一个看不出来的错。改名之后两个轴各自说得清自己是哪一个。
/// </remarks>
/// <param name="HorizontalSign">横向输入，左负右正。</param>
/// <param name="DepthSign">纵深输入，**正为向前（靠近镜头）、负为向后**，与 <see cref="DepthBand"/> 同向。</param>
/// <param name="JumpPressed">本帧刚按下跳跃。</param>
/// <param name="LightPressed">本帧刚按下轻攻击。</param>
/// <param name="HeavyPressed">本帧刚按下重攻击。</param>
/// <param name="DodgePressed">本帧刚按下闪避。</param>
/// <param name="SprintHeld">冲刺键是否按住。</param>
public readonly record struct CombatInput(
    int HorizontalSign,
    int DepthSign,
    bool JumpPressed,
    bool LightPressed,
    bool HeavyPressed,
    bool DodgePressed,
    bool SprintHeld)
{
    /// <summary>没有任何输入的空帧。测试与「无操作」帧用它。</summary>
    public static CombatInput None => new(0, 0, false, false, false, false, false);

    /// <summary>横向方向规整到 −1／0／+1。</summary>
    public int HorizontalDirection => Math.Sign(HorizontalSign);

    /// <summary>纵深方向规整到 −1／0／+1。+1 向前（靠近镜头）。</summary>
    public int DepthDirection => Math.Sign(DepthSign);

    /// <summary>这一帧有任何方向输入吗。闪避取按下瞬间的方向时用它区分「没按方向」（`GP-9`）。</summary>
    public bool HasDirection => HorizontalDirection != 0 || DepthDirection != 0;
}
