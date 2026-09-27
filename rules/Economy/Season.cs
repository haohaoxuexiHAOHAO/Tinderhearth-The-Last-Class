namespace Tinderhearth.Rules.Economy;

/// <summary>一年的四个季节。</summary>
/// <remarks>
/// 它属**时间系统**而不属地块 —— 但时间系统还没有代码，而地块是第一个要读它的东西
/// （换季枯死判定，见设计仓 `design/地块系统.md`），所以它先落在这个命名空间里。
/// 时间系统落地时这个枚举不用改，只是多几个读者。
///
/// **枚举的顺序不参与任何判定。** 换季只问「新季节在不在这种作物声明的那张列表里」，
/// 所以顺序改了、或者往里加第五个季节，都不会静默改坏枯死判定。
/// </remarks>
public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter,
}
