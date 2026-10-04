namespace Tinderhearth.Rules.Economy;

/// <summary>一年的四个季节。</summary>
/// <remarks>
/// 季节本该归时间系统，但时间系统还没有代码，而地块的换季枯死判定是第一个要读它的地方
/// （见设计仓 design/地块系统.md），所以它暂时放在经营这个命名空间里。时间系统写出来之后
/// 这个枚举不用改，只是多几个读者。
///
/// 枚举值的顺序不参与任何判定。换季只问「新季节在不在这种作物声明的列表里」，所以调整顺序、
/// 或者往里加第五个季节，都不会悄悄改坏枯死判定。
/// </remarks>
public enum Season
{
    Spring,
    Summer,
    Autumn,
    Winter,
}
