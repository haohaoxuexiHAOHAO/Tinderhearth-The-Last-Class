namespace Tinderhearth.Rules.Economy;

/// <summary>一年的四个季节。</summary>
/// <remarks>
/// 时间归时间与经营系统，所以季节与日历都在经营这个命名空间里。
///
/// 枚举值的顺序不参与任何判定。一年里的先后写在 <see cref="Calendar.SeasonOrder"/> 那张表里；换季枯死只问
/// 「新季节在不在这种作物声明的列表里」。所以调整这里的顺序不会悄悄改坏日历或枯死判定；往里加一个季节
/// 而不排进那张表，有测试会失败。
/// </remarks>
public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter,
}
