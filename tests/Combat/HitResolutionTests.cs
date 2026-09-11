using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

public class HitResolutionTests
{
    [Fact]
    public void 轻重结算来自集中参数且重击反应更大()
    {
        var light = HitResolution.Resolve(ComboKind.Light);
        var heavy = HitResolution.Resolve(ComboKind.Heavy);
        Assert.Equal(new HitReaction(CombatFeel.LightKnockbackWorldPx, CombatFeel.LightHitstunFrames,
            CombatFeel.LightHitstopFrames, false), light);
        Assert.Equal(new HitReaction(CombatFeel.HeavyKnockbackWorldPx, CombatFeel.HeavyHitstunFrames,
            CombatFeel.HeavyHitstopFrames, true), heavy);
        Assert.Equal(0, light.KnockbackWorldPx);   // 作者 2026-09-11 定：轻击不击退
        Assert.True(light.HitstunFrames > 0);
        Assert.True(light.HitstopFrames > 0);
        Assert.True(heavy.KnockbackWorldPx > light.KnockbackWorldPx);
        Assert.True(heavy.HitstunFrames > light.HitstunFrames);
        Assert.True(heavy.HitstopFrames > light.HitstopFrames);
    }

    [Theory]
    [InlineData(ComboKind.None)]
    [InlineData((ComboKind)(-1))]
    [InlineData((ComboKind)3)]
    public void 非攻击不能结算为重击(ComboKind weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HitResolution.Resolve(weight));
    }

    [Theory]
    [InlineData(ComboKind.Light)]
    [InlineData(ComboKind.Heavy)]
    public void 命中顿帧冻结恰好指定帧然后恢复载体推进(ComboKind weight)
    {
        var reaction = HitResolution.Resolve(weight);
        var statuses = new StatusEffects();
        var timer = new HitstopTimer();
        statuses.Apply(StatusKind.Hitstun, reaction.HitstunFrames);
        timer.Begin(reaction.HitstopFrames);
        for (var i = 0; i < reaction.HitstopFrames; i++)
        {
            Assert.True(timer.IsActive);
            Assert.True(timer.Tick());
            Assert.Equal(reaction.HitstopFrames - i - 1, timer.RemainingFrames);
            Assert.Equal(reaction.HitstunFrames, statuses.Get(StatusKind.Hitstun).RemainingFrames);
        }
        Assert.False(timer.IsActive);
        Assert.False(timer.Tick());
        statuses.Tick();
        Assert.Equal(reaction.HitstunFrames - 1, statuses.Get(StatusKind.Hitstun).RemainingFrames);
        Assert.False(timer.Tick());
        Assert.Equal(0, timer.RemainingFrames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void 非法顿帧时长不能修改倒计时(int frames)
    {
        var timer = new HitstopTimer();
        timer.Begin(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Begin(frames));
        Assert.Equal(2, timer.RemainingFrames);
    }

    [Fact]
    public void 顿帧可刷新缩短延长且一帧也恰好消费一次()
    {
        var timer = new HitstopTimer();
        Assert.False(timer.Tick());
        timer.Begin(int.MaxValue);
        timer.Begin(int.MaxValue);
        Assert.True(timer.Tick());
        Assert.Equal(int.MaxValue - 1, timer.RemainingFrames);
        timer.Begin(1);
        Assert.True(timer.Tick());
        Assert.False(timer.Tick());
        timer.Begin(2);
        Assert.True(timer.Tick());
        Assert.True(timer.Tick());
        Assert.False(timer.Tick());
    }
}
