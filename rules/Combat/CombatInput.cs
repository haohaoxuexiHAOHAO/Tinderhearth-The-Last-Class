namespace Tinderhearth.Rules.Combat;
/// <summary>一个物理帧的战斗输入快照。</summary>
/// <remarks>
/// 规则层不认识 Godot 的 <c>InputRouter</c>：引擎层每帧把玩家或 AI 的意图压成这个结构再喂进来，
/// 于是「玩家不等于主角」在类型上就成立 —— 换成 AI 只是换一个 <see cref="CombatInput"/> 的来源，
/// 两台状态机不必知道。
///
/// 边沿与持续态分开：攻击、跳跃、闪避取「本帧刚按下」，冲刺取「按住」。冲刺是修饰键式的持续
/// 位移，按住才冲；其余三者都是一次触发。
///
/// 移动是两个独立的轴，不是一个二维量：<see cref="HorizontalSign"/> 走横向，
/// <see cref="DepthSign"/> 走纵深。两个字段各自说得清自己是哪一个轴，所以「把纵深接到横向字段
/// 上」不会变成一个看不出来的错。
/// </remarks>
/// <param name="HorizontalSign">横向输入，左负右正。</param>
/// <param name="DepthSign">纵深输入，正为向前、靠近镜头，负为向后，与 <see cref="DepthBand"/> 同向。</param>
/// <param name="JumpPressed">本帧刚按下跳跃。</param>
/// <param name="LightPressed">本帧刚按下轻攻击。</param>
/// <param name="HeavyPressed">本帧刚按下重攻击。</param>
/// <param name="DodgePressed">本帧刚按下闪避。</param>
/// <param name="RunHeld">奔跑键是否按住。设计文档里的「冲刺」指同一件事，见 <c>InputActions.Run</c>。</param>
public readonly record struct CombatInput(
    int HorizontalSign,
    int DepthSign,
    bool JumpPressed,
    bool LightPressed,
    bool HeavyPressed,
    bool DodgePressed,
    bool RunHeld)
{
    /// <summary>没有任何输入的空帧。测试与「这一帧不操作」用它。</summary>
    public static CombatInput None => new(0, 0, false, false, false, false, false);
    /// <summary>横向方向规整到 −1、0 或 +1。</summary>
    public int HorizontalDirection => Math.Sign(HorizontalSign);
    /// <summary>纵深方向规整到 −1、0 或 +1。+1 向前，也就是靠近镜头。</summary>
    public int DepthDirection => Math.Sign(DepthSign);
    /// <summary>这一帧有任何方向输入吗。闪避取按下瞬间的方向，靠它区分「一个方向都没按」。</summary>
    public bool HasDirection => HorizontalDirection != 0 || DepthDirection != 0;
}
