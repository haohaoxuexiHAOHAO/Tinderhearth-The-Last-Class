using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>战斗外层唯一物理时钟；命中帧 Begin 不消费冻结帧。</summary>
public sealed class Hitstop
{
    private readonly HitstopTimer _timer = new();
    /// <summary>尚待冻结帧数。</summary>
    public int RemainingFrames => _timer.RemainingFrames;
    /// <summary>消费外层时钟；返回本帧是否冻结（包括递减至零那帧）。</summary>
    public bool Tick() => _timer.Tick();
    /// <summary>命中结算完成后开启冻结。</summary>
    public void Begin(int frames) => _timer.Begin(frames);
}
