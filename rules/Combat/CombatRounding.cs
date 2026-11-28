namespace Tinderhearth.Rules.Combat;

/// <summary>战斗里向下取整成整数的那一处：面板的点数类与伤害都走它。</summary>
/// <remarks>
/// 乘出来本该是整数的，浮点数会落在差一点点的下面：100 × 1.15 得 114.99999999999999，直接取整就少了 1 点。
/// 所以先把小数点后九位以外的尾巴抹掉再取整；面板与伤害的数离九位小数还远，抹掉的只有浮点误差。
/// </remarks>
internal static class CombatRounding
{
    private const int KeptDecimals = 9;

    /// <summary>向下取整。</summary>
    public static int Floor(double value) => (int)Math.Floor(Math.Round(value, KeptDecimals));
}
