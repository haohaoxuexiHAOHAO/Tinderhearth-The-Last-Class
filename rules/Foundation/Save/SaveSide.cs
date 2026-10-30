namespace Tinderhearth.Rules.Foundation.Save;

/// <summary>一个分片进哪一侧：正式存档，或临时续玩点。</summary>
/// <remarks>
/// 判据一句话：跨过一次睡眠还成立的进正式存档，只在这次退出到下次继续之间成立的进续玩点（设计仓
/// design/存档系统.md 的「进哪一侧：判据一句话」一节）。没有「两边都进」这个取值：想两边都进的东西其实是两样。
///
/// 取值从 1 起，0 留给「没填」：声明里漏了这一格就是 0，注册时当场被拒。
/// </remarks>
public enum SaveSide
{
    /// <summary>正式存档：只在睡眠时写。</summary>
    FormalSave = 1,

    /// <summary>临时续玩点：退出、进出场景时写，读取后即失效。</summary>
    ResumePoint = 2,
}
