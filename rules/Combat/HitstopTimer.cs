namespace Tinderhearth.Rules.Combat;

/// <summary>有限帧顿帧计时器；只计时，不冻结引擎。</summary>
/// <remarks>
/// 外层每个物理帧先调用 Tick，返回 true 则跳过本帧战斗及相机推进。
/// 包括倒数最后一帧也返回 true，下一次才恢复。此时钟不能放进被顿帧跳过的逻辑里。
/// 命中帧在战斗结算后 Begin(N)，冻结随后 N 帧；再次 Begin 用新值替换剩余时间。
/// </remarks>
public sealed class HitstopTimer
{
    /// <summary>尚需冻结的帧数。</summary>
    public int RemainingFrames { get; private set; }

    /// <summary>还有待消费的冻结帧。</summary>
    public bool IsActive => RemainingFrames > 0;

    /// <summary>开启正整数帧的顿帧；非法输入不改变现有倒计时。</summary>
    public void Begin(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        RemainingFrames = frames;
    }

    /// <summary>消费一个物理帧，返回这一帧是否必须冻结，而非递减后的状态。</summary>
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
