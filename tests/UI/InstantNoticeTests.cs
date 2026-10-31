using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>即时提示那一行：出一条、停够了收起，新的一条顶掉旧的，不排队。</summary>
/// <remarks>
/// 这几条要有测试，因为失效方式都不报错：排了队，提示就在玩家换了下一个动作之后才冒出来；顶掉时停留不从头算，
/// 新的那一条一闪就没；停留填成零，出了等于没出。停多久是测试用值，取 2 的幂好让逐帧累加没有舍入。
/// </remarks>
public class InstantNoticeTests
{
    private const double Hold = 0.5;
    private const double Frame = 1.0 / 64;

    /// <summary>一帧一帧往前走。</summary>
    private static void Run(InstantNotice notice, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            notice.Advance(Frame);
        }
    }

    [Fact]
    public void 出一条之后停够那么久就收起()
    {
        var notice = new InstantNotice(Hold);
        Assert.Null(notice.Line);

        notice.Show("背包放不下");
        Run(notice, 31);
        Assert.Equal("背包放不下", notice.Line);

        Run(notice, 1);
        Assert.Null(notice.Line);
    }

    [Fact]
    public void 新的一条顶掉旧的而且停留从头算()
    {
        var notice = new InstantNotice(Hold);
        notice.Show("手上这一格是空的");
        Run(notice, 24);

        notice.Show("背包放不下");
        Run(notice, 24);

        // 旧的那一条若接着它自己的停留走，这时早就收了；新的那一条从头算，所以还在。
        Assert.Equal("背包放不下", notice.Line);
        Run(notice, 8);
        Assert.Null(notice.Line);
    }

    [Fact]
    public void 不排队_新的那一条收起之后旧的不会再冒出来()
    {
        var notice = new InstantNotice(Hold);
        notice.Show("手上这一格是空的");
        notice.Show("背包放不下");

        Run(notice, 32);
        Assert.Null(notice.Line);

        Run(notice, 64);
        Assert.Null(notice.Line);
    }

    [Fact]
    public void 同一句再出一次停留也从头算()
    {
        // 玩家对着同一格连按两下都被拒，第二下也要停够那么久，不能接着第一下剩的那一点收掉。
        var notice = new InstantNotice(Hold);
        notice.Show("背包放不下");
        Run(notice, 24);

        notice.Show("背包放不下");
        Run(notice, 24);

        Assert.Equal("背包放不下", notice.Line);
    }

    [Fact]
    public void 一次过很久也只是收起()
    {
        var notice = new InstantNotice(Hold);
        notice.Show("背包放不下");

        notice.Advance(10);
        Assert.Null(notice.Line);

        notice.Advance(10);
        Assert.Null(notice.Line);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void 停留不是有限的正数就当场抛(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstantNotice(seconds));
    }

    [Theory]
    [InlineData(-Frame)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void 过去的时间为负或不是有限数就抛(double seconds)
    {
        var notice = new InstantNotice(Hold);

        Assert.Throws<ArgumentOutOfRangeException>(() => notice.Advance(seconds));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 出一条空的就抛(string line)
    {
        // 空的一条会让那一行显出来却什么都没写，看着像是坏了。
        var notice = new InstantNotice(Hold);

        Assert.Throws<ArgumentException>(() => notice.Show(line));
    }
}
