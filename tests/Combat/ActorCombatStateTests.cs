using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

public class ActorCombatStateTests
{
    [Fact]
    public void AttackWinsSimultaneousJumpDodgeAndMove()
    {
        var state = new ActorCombatState();
        state.Tick(new(1, 1, true, true, true, true, true), true);
        Assert.Equal(ComboKind.Light, state.Combo.Kind);
        Assert.Equal(MotorPhase.Grounded, state.Motor.Phase);
        Assert.Equal(0, state.Motor.HorizontalVelocity);
        Assert.Equal(0, state.Motor.VerticalVelocity);
        // 三个轴一起定身（`GP-15`）：同帧连纵深键也按着，纵深仍然一像素不动。
        Assert.Equal(0, state.Motor.DepthVelocity);
        Assert.Equal(DepthBand.CenterWorldPx, state.Motor.DepthWorldPx);
    }

    [Fact]
    public void DodgeRejectsAttackIncludingExitFrame()
    {
        var state = new ActorCombatState();
        state.Tick(new(1, 0, false, false, false, true, false), true);
        for (var i = 1; i < CombatFeel.DodgeDurationFrames; i++)
        {
            state.Tick(new(0, 0, false, true, true, false, false), true);
            Assert.False(state.Combo.IsAttacking);
        }
        state.Tick(new(0, 0, false, false, true, false, false), true);
        Assert.Equal(ComboKind.Heavy, state.Combo.Kind);
    }

    [Fact]
    public void CollisionFeedbackDoesNotAdvanceTimersAndClearsAirCombo()
    {
        var state = new ActorCombatState();
        state.Tick(new(0, 0, true, false, false, false, false), true);
        state.Tick(new(0, 0, false, true, false, false, false), false);
        state.Motor.Statuses.Apply(StatusKind.Invulnerable, 4);
        state.AfterMove(false, true);
        Assert.Equal(0, state.Motor.VerticalVelocity);
        Assert.Equal(0, state.Combo.FrameInPhase);
        Assert.Equal(4, state.Motor.Statuses.Get(StatusKind.Invulnerable).RemainingFrames);
        state.Tick(CombatInput.None, false);
        Assert.True(state.Motor.VerticalVelocity > 0);
        state.AfterMove(true, false);
        Assert.False(state.Combo.IsAttacking);
        Assert.Equal(MotorPhase.Grounded, state.Motor.Phase);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public void FullChainUsesActualWindow(bool heavy, int length)
    {
        var state = new ActorCombatState();
        var press = new CombatInput(0, 0, false, !heavy, heavy, false, false);
        state.Tick(press, true);
        for (var step = 1; step < length; step++)
        {
            for (var frame = 0; !state.Combo.IsComboWindowOpen && frame < 60; frame++)
                state.Tick(CombatInput.None, true);
            Assert.True(state.Combo.IsComboWindowOpen);
            state.Tick(press, true);
            Assert.Equal(step, state.Combo.Step);
            Assert.Equal(0, state.Combo.FrameInPhase);
        }
        for (var frame = 0; frame < 60; frame++) state.Tick(CombatInput.None, true);
        Assert.False(state.Combo.IsAttacking);
    }
}
