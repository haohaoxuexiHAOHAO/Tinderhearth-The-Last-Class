using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

public class StatusEffectsTests
{
    [Theory]
    [InlineData(StatusKind.Hitstun)]
    [InlineData(StatusKind.Invulnerable)]
    public void 状态立即生效且不可主动解除到期才移除(StatusKind kind)
    {
        var statuses = new StatusEffects();
        Assert.False(statuses.Has(kind));
        statuses.Apply(kind, 2);
        Assert.Equal(new StatusEffect(kind, 2, false), statuses.Get(kind));
        Assert.False(statuses.TryRemove(kind));
        Assert.Equal(2, statuses.Get(kind).RemainingFrames);
        statuses.Tick();
        Assert.True(statuses.Has(kind));
        statuses.Tick();
        Assert.False(statuses.Has(kind));
        statuses.Tick();
        Assert.Equal(0, statuses.Get(kind).RemainingFrames);
        Assert.False(statuses.TryRemove(kind));
    }

    [Theory]
    [InlineData(StatusKind.Hitstun, 1)]
    [InlineData(StatusKind.Hitstun, 10)]
    [InlineData(StatusKind.Invulnerable, 1)]
    [InlineData(StatusKind.Invulnerable, 10)]
    public void 刷新替换剩余帧而非相加或取最大(StatusKind kind, int frames)
    {
        var statuses = new StatusEffects();
        statuses.Apply(kind, 5);
        statuses.Tick();
        statuses.Apply(kind, frames);
        Assert.Equal(frames, statuses.Get(kind).RemainingFrames);
        for (var i = 0; i < frames; i++)
        {
            Assert.True(statuses.Has(kind));
            statuses.Tick();
        }
        Assert.False(statuses.Has(kind));
    }

    [Theory]
    [InlineData(StatusKind.Hitstun, 0)]
    [InlineData(StatusKind.Hitstun, -1)]
    [InlineData(StatusKind.Invulnerable, 0)]
    [InlineData(StatusKind.Invulnerable, int.MinValue)]
    public void 非正时长不能绕过不可解除约束(StatusKind kind, int frames)
    {
        var statuses = new StatusEffects();
        statuses.Apply(kind, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => statuses.Apply(kind, frames));
        Assert.Equal(3, statuses.Get(kind).RemainingFrames);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void 未知状态在所有入口拒绝且不修改有效状态(int value)
    {
        var statuses = new StatusEffects();
        statuses.Apply(StatusKind.Hitstun, 3);
        var kind = (StatusKind)value;
        Assert.Throws<ArgumentOutOfRangeException>(() => statuses.Apply(kind, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => statuses.Get(kind));
        Assert.Throws<ArgumentOutOfRangeException>(() => statuses.Has(kind));
        Assert.Throws<ArgumentOutOfRangeException>(() => statuses.TryRemove(kind));
        Assert.Equal(3, statuses.Get(StatusKind.Hitstun).RemainingFrames);
    }

    [Fact]
    public void 两种状态独立且载体之间不共享状态()
    {
        var statuses = new StatusEffects();
        var other = new StatusEffects();
        statuses.Apply(StatusKind.Hitstun, 1);
        statuses.Apply(StatusKind.Invulnerable, 2);
        var snapshot = statuses.Get(StatusKind.Invulnerable);
        statuses.Tick();
        Assert.False(statuses.Has(StatusKind.Hitstun));
        Assert.Equal(1, statuses.Get(StatusKind.Invulnerable).RemainingFrames);
        Assert.Equal(2, snapshot.RemainingFrames);
        Assert.False(other.Has(StatusKind.Invulnerable));
        statuses.Apply(StatusKind.Hitstun, 1);
        Assert.True(statuses.Has(StatusKind.Hitstun));
    }

    [Fact]
    public void 最大整数时长刷新不溢出()
    {
        var statuses = new StatusEffects();
        statuses.Apply(StatusKind.Hitstun, int.MaxValue);
        statuses.Apply(StatusKind.Hitstun, int.MaxValue);
        statuses.Tick();
        Assert.Equal(int.MaxValue - 1, statuses.Get(StatusKind.Hitstun).RemainingFrames);
    }
}
