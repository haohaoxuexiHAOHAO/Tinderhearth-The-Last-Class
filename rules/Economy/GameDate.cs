namespace Tinderhearth.Rules.Economy;

/// <summary>日历上的一天：第几年、哪一季、这一季的第几天。年与日都从 1 数起。</summary>
/// <remarks>
/// 构造时就校验，所以存档里写着第 0 年或这一季第 31 天时当场被拒，而不是带着一个不存在的日期往下走。
/// <c>default</c> 不是合法的一天（第 0 年第 0 天），日期只从构造或 <see cref="Calendar.StartDate"/> 来。
///
/// 年份没有上限：正典写明没有全局日期上限。
/// </remarks>
public readonly record struct GameDate
{
    public GameDate(int year, Season season, int day)
    {
        if (year < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year), $"年从 1 数起，实际 {year}");
        }

        if (Calendar.IndexOf(season) < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(season), $"季节 {season} 不在日历的季节先后表里（{nameof(Calendar)}.{nameof(Calendar.SeasonOrder)}）");
        }

        if (day < 1 || day > Calendar.DaysPerSeason)
        {
            throw new ArgumentOutOfRangeException(
                nameof(day), $"一季的第几天要在 1 到 {Calendar.DaysPerSeason} 之间，实际 {day}");
        }

        Year = year;
        Season = season;
        Day = day;
    }

    /// <summary>第几年，从 1 数起。</summary>
    public int Year { get; }

    /// <summary>哪一季。</summary>
    public Season Season { get; }

    /// <summary>这一季的第几天，从 1 数起。</summary>
    public int Day { get; }

    /// <summary>下一天。一季最后一天之后是下一季第一天，最后一季过完进下一年。</summary>
    public GameDate Next()
    {
        if (Day < Calendar.DaysPerSeason)
        {
            return new GameDate(Year, Season, Day + 1);
        }

        var index = Calendar.IndexOf(Season);
        return index + 1 < Calendar.SeasonOrder.Count
            ? new GameDate(Year, Calendar.SeasonOrder[index + 1], 1)
            : new GameDate(checked(Year + 1), Calendar.SeasonOrder[0], 1);
    }
}
