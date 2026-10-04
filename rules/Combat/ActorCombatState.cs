namespace Tinderhearth.Rules.Combat;
/// <summary>主角的单帧入口：把运动与连段两台机器按顺序推进一帧，并维持两者的排他关系。</summary>
public sealed class ActorCombatState
{
    public MotorState Motor { get; } = new();
    public ComboStateMachine Combo { get; } = new();
    public void Tick(in CombatInput input, bool onFloor)
    {
        var wasAttacking = Combo.IsAttacking;
        var dodging = Motor.Phase == MotorPhase.Dodge;
        // 闪避期间把攻击键屏掉，因为闪步排他且不可打断。给运动机的「正在出招」取本帧前后两个值的
        // 或，于是连段在本帧结束时这一帧仍算出招中，定身不会提前一帧解除。
        Combo.Tick(!dodging && input.LightPressed, !dodging && input.HeavyPressed, onFloor);
        Motor.Tick(input, onFloor, wasAttacking || Combo.IsAttacking);
    }
    /// <summary>碰撞结果只用来校正状态，不消费第二个逻辑帧。</summary>
    public void AfterMove(bool onFloor, bool onCeiling, bool onWall = false, double horizontalVelocity = 0)
    {
        Motor.AfterMove(onFloor, onCeiling, onWall, horizontalVelocity);
        Combo.AfterMove(onFloor);
    }
}
