namespace Tinderhearth.Rules.Combat;
/// <summary>有限帧数的顿帧计时器。它只计时，不冻结引擎。</summary>
/// <remarks>
/// 外层每个物理帧先调 <see cref="Tick"/>，返回 true 就跳过本帧的战斗与相机推进。倒数最后一帧
/// 也返回 true，下一帧才恢复。
///
/// 这个时钟本身不能放进被顿帧跳过的那段逻辑里，否则它一帧都走不动、永远冻着。命中帧在战斗结算
/// 之后调 <see cref="Begin"/>；再次 Begin 用新值替换剩余时间，不累加。
/// </remarks>
public sealed class HitstopTimer
{
    /// <summary>还要冻结几帧。</summary>
    public int RemainingFrames { get; private set; }
    /// <summary>还有待消费的冻结帧吗。</summary>
    public bool IsActive => RemainingFrames > 0;
    /// <summary>开启正整数帧的顿帧。非法输入抛出，且不改变现有的倒计时。</summary>
    public void Begin(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        RemainingFrames = frames;
    }
    /// <summary>消费一个物理帧。返回的是「这一帧必须冻结吗」，不是递减之后的状态。</summary>
    public bool Tick()
    {
        if (!IsActive)
        {
            return false;
        }
        RemainingFrames--;
        return true;
    }
}
