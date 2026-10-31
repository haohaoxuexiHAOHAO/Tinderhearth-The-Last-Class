using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>输入门面上的那道闸：谁关上的谁打开，有一方还关着就是关着。</summary>
public class InputGateTests
{
    [Fact]
    public void 没人关时是开的()
    {
        Assert.True(new InputGate().IsOpen);
    }

    [Fact]
    public void 关上之后是关着的_关的那一方打开之后才开()
    {
        var gate = new InputGate();

        gate.Close("sleep");
        Assert.False(gate.IsOpen);

        gate.Reopen("sleep");
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void 两方都关着时_一方打开了门面照样关着()
    {
        var gate = new InputGate();
        gate.Close("sleep");
        gate.Close("cutscene");

        gate.Reopen("sleep");

        Assert.False(gate.IsOpen);
        Assert.Equal(["cutscene"], gate.Holders);
    }

    [Fact]
    public void 没关过就打开_或者同一方关两次_当场抛()
    {
        var gate = new InputGate();

        Assert.Throws<InvalidOperationException>(() => gate.Reopen("sleep"));

        gate.Close("sleep");
        Assert.Throws<InvalidOperationException>(() => gate.Close("sleep"));
    }
}
