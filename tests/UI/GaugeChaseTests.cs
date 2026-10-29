using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>资源条的追与留一截：整像素、不越过数值，那一截停够了再缩，停的过程中又挨一下不跳回去。</summary>
/// <remarks>
/// 这几样坏了都不报错：越过数值只表现为条头抖一下，跳回去只表现为浅色那一截闪一下，而那一截本该说清这几下一共掉了多少。
/// 速度与时长是当场编的，取二的幂好让每一帧走几像素算得准：每秒 64 像素、一帧 1/64 秒，一帧正好走一像素。
/// </remarks>
public class GaugeChaseTests
{
    private const double Speed = 64;
    private const double Frame = 1.0 / 64;

    // 半秒，正好 32 帧。
    private const double Hold = 0.5;
    private const int HoldFrames = 32;

    private static GaugeChase At(int length) => new(new GaugeChaseSettings(Speed, Hold), length);

    private static void Run(GaugeChase chase, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            chase.Advance(Frame);
        }
    }

    [Fact]
    public void 开始时停在给的长度上()
    {
        var chase = At(10);

        Assert.Equal(10, chase.Fill);
        Assert.Equal(10, chase.Lag);
        Assert.True(chase.Settled);
        Assert.False(chase.Rising);
    }

    [Fact]
    public void 回复时每帧走一像素走到数值就停()
    {
        var chase = At(10);
        chase.Retarget(40);

        Run(chase, 1);
        Assert.Equal(11, chase.Fill);
        Run(chase, 28);
        Assert.Equal(39, chase.Fill);
        Run(chase, 1);
        Assert.Equal(40, chase.Fill);
        Assert.True(chase.Settled);

        // 数值不变之后它停在数值上，也不留浅色那一截。
        Run(chase, 10);
        Assert.Equal(40, chase.Fill);
        Assert.Equal(40, chase.Lag);
    }

    [Fact]
    public void 一帧隔得很久也不越过数值()
    {
        var chase = At(10);
        chase.Retarget(40);

        chase.Advance(10);

        Assert.Equal(40, chase.Fill);
        Assert.True(chase.Settled);
    }

    [Fact]
    public void 慢的时候攒够一像素才走()
    {
        // 每秒 32 像素，一帧半像素：第一帧不动，第二帧走一像素。
        var chase = new GaugeChase(new GaugeChaseSettings(Speed / 2, Hold), 10);
        chase.Retarget(20);

        Run(chase, 1);
        Assert.Equal(10, chase.Fill);
        Run(chase, 1);
        Assert.Equal(11, chase.Fill);
    }

    [Fact]
    public void 扣减时填充当场落下那一截停够了再缩到数值()
    {
        var chase = At(40);
        chase.Retarget(25);

        Assert.Equal(25, chase.Fill);
        Assert.Equal(40, chase.Lag);
        Assert.False(chase.Rising);
        Assert.False(chase.Settled);

        Run(chase, HoldFrames);
        Assert.Equal(40, chase.Lag);
        Run(chase, 1);
        Assert.Equal(39, chase.Lag);
        Run(chase, 14);
        Assert.Equal(25, chase.Lag);
        Assert.True(chase.Settled);

        // 缩到数值就停，不缩过头。
        Run(chase, 5);
        Assert.Equal(25, chase.Lag);
        Assert.Equal(25, chase.Fill);
    }

    [Fact]
    public void 停的过程中又挨一下那一截不跳回去而且从这一下重新停()
    {
        var chase = At(40);
        chase.Retarget(30);
        Run(chase, HoldFrames / 2);

        chase.Retarget(20);
        Assert.Equal(20, chase.Fill);
        Assert.Equal(40, chase.Lag);

        // 从第二下算起停满才缩：第一下的停顿早就到了，它也不动。
        Run(chase, HoldFrames);
        Assert.Equal(40, chase.Lag);
        Run(chase, 1);
        Assert.Equal(39, chase.Lag);
    }

    [Fact]
    public void 缩的过程中又挨一下那一截停在当时的位置()
    {
        var chase = At(40);
        chase.Retarget(20);
        Run(chase, HoldFrames + 5);
        Assert.Equal(35, chase.Lag);

        chase.Retarget(10);
        Assert.Equal(10, chase.Fill);
        Assert.Equal(35, chase.Lag);

        Run(chase, HoldFrames);
        Assert.Equal(35, chase.Lag);
        Run(chase, 1);
        Assert.Equal(34, chase.Lag);
    }

    [Fact]
    public void 回到那一截之上那一截就没了()
    {
        var chase = At(40);
        chase.Retarget(20);
        chase.Retarget(45);

        Assert.Equal(20, chase.Fill);
        Assert.Equal(20, chase.Lag);
        Assert.True(chase.Rising);

        Run(chase, 1);
        Assert.Equal(21, chase.Fill);
        Assert.Equal(21, chase.Lag);
        Run(chase, 24);
        Assert.Equal(45, chase.Fill);
        Assert.True(chase.Settled);
    }

    [Fact]
    public void 回复没回到那一截之上时它照停照缩到数值()
    {
        var chase = At(40);
        chase.Retarget(20);
        chase.Retarget(30);

        // 回复不重新算停的时间：从扣减那一下起停满 32 帧才缩。
        Run(chase, 10);
        Assert.Equal(30, chase.Fill);
        Assert.Equal(40, chase.Lag);
        Run(chase, HoldFrames - 10);
        Assert.Equal(40, chase.Lag);
        Run(chase, 10);
        Assert.Equal(30, chase.Lag);
        Assert.True(chase.Settled);
    }

    [Fact]
    public void 追的时候挨一下那一截从显示的长度留起()
    {
        var chase = At(10);
        chase.Retarget(40);
        Run(chase, 10);

        chase.Retarget(15);

        // 玩家看到的是 20，不是还没追到的 40：那一截从 20 留起。
        Assert.Equal(15, chase.Fill);
        Assert.Equal(20, chase.Lag);
        Assert.False(chase.Rising);
    }

    [Fact]
    public void 只有往上追的时候才算在涨()
    {
        var chase = At(10);
        Assert.False(chase.Rising);

        chase.Retarget(20);
        Assert.True(chase.Rising);
        Run(chase, 10);
        Assert.False(chase.Rising);

        chase.Retarget(5);
        Assert.False(chase.Rising);
    }

    [Theory]
    [InlineData(0.0, Hold)]
    [InlineData(-1.0, Hold)]
    [InlineData(double.NaN, Hold)]
    [InlineData(double.PositiveInfinity, Hold)]
    [InlineData(Speed, 0.0)]
    [InlineData(Speed, -1.0)]
    public void 两个量不大于零就抛(double speed, double hold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GaugeChaseSettings(speed, hold));
    }

    [Fact]
    public void 负长度与负时间都抛()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => At(-1));

        var chase = At(10);
        Assert.Throws<ArgumentOutOfRangeException>(() => chase.Retarget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => chase.Advance(-Frame));
    }
}
