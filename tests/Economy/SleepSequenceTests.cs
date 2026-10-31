using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>睡下到醒来那一段：第二次推进被拒、输入关着，淡入播完那一刻交回。</summary>
public class SleepSequenceTests
{
    [Fact]
    public void 醒着时睡得下_睡下那一刻输入关上()
    {
        var sleep = new SleepSequence();

        Assert.False(sleep.InputClosed);
        Assert.True(sleep.TryBegin());
        Assert.Equal(SleepPhase.FadingOut, sleep.Phase);
        Assert.True(sleep.InputClosed);
    }

    [Fact]
    public void 睡下之后到弹窗弹出之前_第二次推进被拒()
    {
        var sleep = new SleepSequence();
        sleep.TryBegin();

        Assert.False(sleep.TryBegin());

        sleep.FadeOutFinished();
        Assert.False(sleep.TryBegin());
        Assert.True(sleep.InputClosed);
    }

    [Fact]
    public void 淡入播完那一刻醒来_输入交回_又睡得下()
    {
        var sleep = new SleepSequence();
        sleep.TryBegin();
        sleep.FadeOutFinished();

        sleep.FadeInFinished();

        Assert.Equal(SleepPhase.Awake, sleep.Phase);
        Assert.False(sleep.InputClosed);
        Assert.True(sleep.TryBegin());
    }

    [Fact]
    public void 次序错了当场抛()
    {
        var sleep = new SleepSequence();

        Assert.Throws<InvalidOperationException>(sleep.FadeOutFinished);
        Assert.Throws<InvalidOperationException>(sleep.FadeInFinished);

        sleep.TryBegin();
        Assert.Throws<InvalidOperationException>(sleep.FadeInFinished);
    }

    [Fact]
    public void 中途出了缺陷时回到醒着_输入不会永远关着()
    {
        var sleep = new SleepSequence();
        sleep.TryBegin();
        sleep.FadeOutFinished();

        sleep.Abort();

        Assert.False(sleep.InputClosed);
        Assert.True(sleep.TryBegin());
    }
}
