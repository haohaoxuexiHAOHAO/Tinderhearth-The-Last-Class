namespace Tinderhearth.Rules.Economy;

/// <summary>日历：一年里季节的先后、一季几天，以及开新档那一天。</summary>
/// <remarks>
/// 这几个量都是规则常量，不是要调的数，所以不进参数表也不进检查器。季长与四季在设计仓
/// canon/gameplay/时间与经营.md 的「时间」一节，开局那一天在 canon/gameplay/玩法定位.md 的「开局」一节。
/// 时间单位只有天、季、年三级，没有「月」，所以这里也没有。
/// </remarks>
public static class Calendar
{
    /// <summary>一季有几天。</summary>
    public const int DaysPerSeason = 30;

    /// <summary>一年里季节的先后。最后一季过完是下一年的第一季。</summary>
    /// <remarks>
    /// 先后写成一张表，不读 <see cref="Season"/> 的声明顺序：那样调一下枚举的顺序，日历就悄悄换了。
    /// 枚举里每一个季节都要在这张表里恰好出现一次，有测试盯着。
    /// </remarks>
    public static readonly IReadOnlyList<Season> SeasonOrder =
        [Season.Spring, Season.Summer, Season.Autumn, Season.Winter];

    /// <summary>开新档那一天：第一年春季第一天。</summary>
    /// <remarks>
    /// 为什么从春季开局写在设计仓 canon/gameplay/玩法定位.md 的「开局」一节。写成属性而不是静态字段，
    /// 是为了不依赖本类里静态字段的初始化先后：构造一个日期要先读 <see cref="SeasonOrder"/>。
    /// </remarks>
    public static GameDate StartDate => new(1, Season.Spring, 1);

    /// <summary>这个季节在一年里排第几（0 起）；不在 <see cref="SeasonOrder"/> 里时为 -1。</summary>
    public static int IndexOf(Season season) =>
        SeasonOrder.Select((s, i) => s == season ? i : -1).FirstOrDefault(i => i >= 0, -1);
}
