namespace Tinderhearth.Rules.Combat;

/// <summary>主角的单一帧入口，协调运动与连段的排他性。</summary>
public sealed class ActorCombatState
{
    public MotorState Motor { get; } = new();
    public ComboStateMachine Combo { get; } = new();

    public void Tick(in CombatInput input, bool onFloor)
    {
        var wasAttacking = Combo.IsAttacking;
        var dodging = Motor.Phase == MotorPhase.Dodge;
        Combo.Tick(!dodging && input.LightPressed, !dodging && input.HeavyPressed, onFloor);
        Motor.Tick(input, onFloor, wasAttacking || Combo.IsAttacking);
    }

    /// <summary>碰撞结果只校正状态，不消费第二个逻辑帧。</summary>
    public void AfterMove(bool onFloor, bool onCeiling, bool onWall = false, double horizontalVelocity = 0)
    {
        Motor.AfterMove(onFloor, onCeiling, onWall, horizontalVelocity);
        Combo.AfterMove(onFloor);
    }
}
