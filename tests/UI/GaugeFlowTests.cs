using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>中段挪的那一段：整像素地挪，挪满一段回到原样，每一格取的是原图里的哪一格。</summary>
/// <remarks>
/// 这几样坏了都不报错：不按整像素挪会在屏上插出半格，挪满一段回不到原样会让花纹在接缝处跳一下。
/// 速度与时长取二的幂，好让每一帧挪几像素算得准：每秒 64 像素、一帧 1/64 秒，一帧正好一像素。
/// </remarks>
public class GaugeFlowTests
{
    private const double Speed = 64;
    private const double Frame = 1.0 / 64;
    private const int Period = 12;

    private static void Run(GaugeFlow flow, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            flow.Advance(Frame);
        }
    }

    [Fact]
    public void 开始是原样()
    {
        Assert.Equal(0, new GaugeFlow(Speed, Period).Offset);
    }

    [Fact]
    public void 慢的时候攒够一像素才挪()
    {
        // 每秒 32 像素，一帧半像素：第一帧不动，第二帧挪一像素。
        var flow = new GaugeFlow(Speed / 2, Period);

        Run(flow, 1);
        Assert.Equal(0, flow.Offset);
        Run(flow, 1);
        Assert.Equal(1, flow.Offset);
    }

    [Fact]
    public void 挪满一段回到原样()
    {
        var flow = new GaugeFlow(Speed, Period);

        Run(flow, Period - 1);
        Assert.Equal(Period - 1, flow.Offset);
        Run(flow, 1);
        Assert.Equal(0, flow.Offset);
        Run(flow, 1);
        Assert.Equal(1, flow.Offset);
    }

    [Fact]
    public void 一帧隔得很久也落在一段之内()
    {
        var flow = new GaugeFlow(Speed, Period);

        // 十秒挪 640 像素，640 除以 12 余 4。
        flow.Advance(10);

        Assert.Equal(640 % Period, flow.Offset);
    }

    [Fact]
    public void 没挪的时候每一格就是原图那一格()
    {
        for (var i = 0; i < Period; i++)
        {
            Assert.Equal(i, GaugeFlow.SourceIndex(i, 0, Period));
            Assert.Equal(i, GaugeFlow.SourceIndex(i, Period, Period));
        }
    }

    [Fact]
    public void 挪一格之后每一格取它后面那一格并且绕回来接上()
    {
        // 原图第 j 格落到第 j + 1 格：第 5 格显示的是原来的第 4 格，第 0 格接上原来的最后一格。
        Assert.Equal(4, GaugeFlow.SourceIndex(5, 1, Period));
        Assert.Equal(Period - 1, GaugeFlow.SourceIndex(0, 1, Period));

        // 每一格都取到了，一格不漏、一格不重：挪出来的那一张仍然只是原图那一段换了个起点。
        var taken = Enumerable.Range(0, Period).Select(i => GaugeFlow.SourceIndex(i, 5, Period)).Order();
        Assert.Equal(Enumerable.Range(0, Period), taken);
    }

    [Theory]
    [InlineData(0.0, Period)]
    [InlineData(-1.0, Period)]
    [InlineData(double.NaN, Period)]
    [InlineData(double.PositiveInfinity, Period)]
    [InlineData(Speed, 0)]
    [InlineData(Speed, -1)]
    public void 速度不大于零或一段短于一像素就抛(double speed, int period)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GaugeFlow(speed, period));
    }

    [Fact]
    public void 负时间抛()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GaugeFlow(Speed, Period).Advance(-Frame));
    }

    [Theory]
    [InlineData(0.0, Speed)]
    [InlineData(-1.0, Speed)]
    [InlineData(Speed, 0.0)]
    [InlineData(Speed, double.NaN)]
    public void 两个挪的速度不大于零就抛(double flow, double bubble)
    {
        var chase = new GaugeChaseSettings(Speed, 0.5);

        Assert.Throws<ArgumentOutOfRangeException>(() => new GaugeMotionSettings(chase, flow, bubble));
    }
}
