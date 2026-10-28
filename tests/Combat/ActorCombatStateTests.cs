using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

public class ActorCombatStateTests
{
    /// <summary>只按轻攻击的一帧输入。</summary>
    private static CombatInput Light => new(0, 0, false, true, false, false);

    [Fact]
    public void AttackWinsSimultaneousJumpDodgeAndMove()
    {
        var state = new ActorCombatState();
        state.Tick(new(1, 1, true, true, true, true), true);
        Assert.True(state.Combo.IsAttacking);
        Assert.Equal(MotorPhase.Grounded, state.Motor.Phase);
        Assert.Equal(0, state.Motor.HorizontalVelocity);
        Assert.Equal(0, state.Motor.VerticalVelocity);
        // 三个轴一起定身：同帧连纵深键也按着，纵深仍然一像素不动。
        Assert.Equal(0, state.Motor.DepthVelocity);
        Assert.Equal(DepthBand.CenterWorldPx, state.Motor.DepthWorldPx);
    }

    [Fact]
    public void DodgeRejectsAttackIncludingExitFrame()
    {
        var state = new ActorCombatState();
        state.Tick(new(1, 0, false, false, true, false), true);
        for (var i = 1; i < CombatFeel.DodgeDurationFrames; i++)
        {
            state.Tick(Light, true);
            Assert.False(state.Combo.IsAttacking);
        }
        // 闪避期间按下的攻击被屏掉、不进缓冲，所以这里起手的是一次新的按下。
        state.Tick(Light, true);
        Assert.True(state.Combo.IsAttacking);
        Assert.Equal(0, state.Combo.Step);
    }

    [Fact]
    public void CollisionFeedbackDoesNotAdvanceTimersAndClearsAirCombo()
    {
        var state = new ActorCombatState();
        state.Tick(new(0, 0, true, false, false, false), true);
        state.Tick(Light, false);
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

    [Fact]
    public void FullChainUsesActualWindow()
    {
        var state = new ActorCombatState();
        state.Tick(Light, true);
        for (var step = 1; step < CombatFeel.LightChainLength; step++)
        {
            for (var frame = 0; !state.Combo.IsComboWindowOpen && frame < 60; frame++)
                state.Tick(CombatInput.None, true);
            Assert.True(state.Combo.IsComboWindowOpen);
            state.Combo.RegisterHit();   // 续段要先命中确认
            state.Tick(Light, true);
            Assert.Equal(step, state.Combo.Step);
            Assert.Equal(0, state.Combo.FrameInPhase);
        }
        for (var frame = 0; frame < 60; frame++) state.Tick(CombatInput.None, true);
        Assert.False(state.Combo.IsAttacking);
    }

    /// <summary>
    /// 端到端的主路径：只按轻攻击，连段每一段都出手、都打中，每一下都按轻的那一组结算。
    /// </summary>
    /// <remarks>
    /// 它把按键、连段机与命中结算串在一起走一遍：判定窗开时打中（引擎层在那时调
    /// <see cref="ComboStateMachine.RegisterHit"/>），反应按这一段的轻重取。普攻只剩轻攻击一种之后，
    /// 这条链上任何一处又按「按了哪个键」取轻重，都会在这里对不上。
    /// </remarks>
    [Fact]
    public void 只用轻攻击打完一整条连段_每一下都按轻的那一组结算()
    {
        var state = new ActorCombatState();
        var reactions = new List<HitReaction>();
        state.Tick(Light, true);
        for (var step = 0; step < CombatFeel.LightChainLength; step++)
        {
            for (var frame = 0; !state.Combo.IsHitActive && frame < 60; frame++)
                state.Tick(CombatInput.None, true);
            Assert.True(state.Combo.IsHitActive);
            Assert.Equal(step, state.Combo.Step);

            state.Combo.RegisterHit();
            reactions.Add(HitResolution.Resolve(state.Combo.Weight));

            if (step + 1 < CombatFeel.LightChainLength)
            {
                for (var frame = 0; !state.Combo.IsComboWindowOpen && frame < 60; frame++)
                    state.Tick(CombatInput.None, true);
                state.Tick(Light, true);
            }
        }

        Assert.Equal(CombatFeel.LightChainLength, reactions.Count);
        Assert.All(reactions, r => Assert.Equal(HitResolution.Resolve(HitWeight.Light), r));
        Assert.All(reactions, r => Assert.Equal(0, r.KnockbackWorldPx));   // 轻的不击退，连段留在射程里

        for (var frame = 0; frame < 60; frame++) state.Tick(CombatInput.None, true);
        Assert.False(state.Combo.IsAttacking);
    }

    /// <summary>端到端的失败路径：第一段打空，续段窗里再按也不续，走完回到待机。</summary>
    [Fact]
    public void 只用轻攻击_第一段打空就不续()
    {
        var state = new ActorCombatState();
        state.Tick(Light, true);
        for (var frame = 0; !state.Combo.IsComboWindowOpen && frame < 60; frame++)
            state.Tick(CombatInput.None, true);
        Assert.True(state.Combo.IsComboWindowOpen);

        state.Tick(Light, true);   // 没有 RegisterHit：这一段打空了
        Assert.Equal(0, state.Combo.Step);

        for (var frame = 0; frame < 60; frame++) state.Tick(CombatInput.None, true);
        Assert.False(state.Combo.IsAttacking);
        Assert.Equal(0, state.Combo.Step);
    }
}
