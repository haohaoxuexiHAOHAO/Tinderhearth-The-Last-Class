namespace Tinderhearth.Rules.Combat;

/// <summary>
/// 一个物理帧的战斗输入快照（`GP-10`）。
/// </summary>
/// <remarks>
/// 规则层不认识 Godot 的 <c>InputRouter</c>：引擎层每帧把玩家（或 A2 的 AI）意图压成这个结构
/// 再喂进来，于是「玩家 ≠ 主角」在类型上成立 —— 换成 AI 只是换一个 <see cref="CombatInput"/>
/// 的来源，两台状态机不必知道。
///
/// **边沿与持续态分开**：攻击、跳跃、闪避取「本帧刚按下」（边沿），冲刺取「按住」（持续）。
/// 冲刺是修饰键式的持续位移，正典要求它显式（按住才冲），其余三者是一次触发。
/// </remarks>
public readonly record struct CombatInput(
    int MoveSign,
    bool JumpPressed,
    bool LightPressed,
    bool HeavyPressed,
    bool DodgePressed,
    bool SprintHeld)
{
    /// <summary>没有任何输入的空帧。测试与「无操作」帧用它。</summary>
    public static CombatInput None => new(0, false, false, false, false, false);

    /// <summary>移动方向规整到 −1／0／+1。侧视只用左右，竖向输入不驱动移动。</summary>
    public int Direction => Math.Sign(MoveSign);
}
