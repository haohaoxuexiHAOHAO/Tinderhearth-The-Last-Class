using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>日历：日期按天、季、年往前走，开新档从开局那一天开始。</summary>
/// <remarks>
/// 季节的先后（春、夏、秋、冬）在这里写死，是在钉日历与正典对得上；一季几天读常量，改它不该让这里失败。
/// </remarks>
public class CalendarTests
{
    [Fact]
    public void 一季之内往前推一天()
    {
        Assert.Equal(new GameDate(1, Season.Spring, 4), new GameDate(1, Season.Spring, 3).Next());
    }

    [Fact]
    public void 一季最后一天之后是下一季第一天()
    {
        Assert.Equal(new GameDate(1, Season.Summer, 1), new GameDate(1, Season.Spring, Calendar.DaysPerSeason).Next());
        Assert.Equal(new GameDate(1, Season.Autumn, 1), new GameDate(1, Season.Summer, Calendar.DaysPerSeason).Next());
        Assert.Equal(new GameDate(1, Season.Winter, 1), new GameDate(1, Season.Autumn, Calendar.DaysPerSeason).Next());
    }

    [Fact]
    public void 最后一季最后一天之后是下一年第一季第一天()
    {
        Assert.Equal(new GameDate(2, Season.Spring, 1), new GameDate(1, Season.Winter, Calendar.DaysPerSeason).Next());
    }

    /// <summary>没有全局日期上限，所以很大的年份也照样进一年。</summary>
    [Fact]
    public void 年份没有上限()
    {
        Assert.Equal(
            new GameDate(1_000_001, Season.Spring, 1),
            new GameDate(1_000_000, Season.Winter, Calendar.DaysPerSeason).Next());
    }

    /// <summary>从开局那一天一天天推，推满一年正好是下一年的同一天。它钉的是一年的总天数没有多一天或少一天。</summary>
    [Fact]
    public void 推满一年回到下一年的同一天()
    {
        var date = Calendar.StartDate;
        for (var i = 0; i < Calendar.DaysPerSeason * Calendar.SeasonOrder.Count; i++)
        {
            date = date.Next();
        }

        Assert.Equal(new GameDate(Calendar.StartDate.Year + 1, Calendar.StartDate.Season, Calendar.StartDate.Day), date);
    }

    /// <summary>往枚举里加一个季节而不排进日历，这一条失败：日历会在那个季节上停住。</summary>
    [Fact]
    public void 季节先后表里每个季节恰好出现一次()
    {
        Assert.Equal(Enum.GetValues<Season>().Order(), Calendar.SeasonOrder.Order());
        Assert.Equal(Calendar.SeasonOrder.Count, Calendar.SeasonOrder.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, Calendar.DaysPerSeason + 1)]
    public void 不合法的年与日被拒(int year, int day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(year, Season.Spring, day));
    }

    [Fact]
    public void 不在季节先后表里的季节被拒()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(1, (Season)99, 1));
    }

    [Fact]
    public void 开新档从第一年春季第一天开始()
    {
        var world = WorldState.NewGame(PlotGrid.Rectangle(new GridCell(0, 0), 1, 1, PlotState.Tilled));

        Assert.Equal(new GameDate(1, Season.Spring, 1), Calendar.StartDate);
        Assert.Equal(Calendar.StartDate, world.Date);
    }
}
