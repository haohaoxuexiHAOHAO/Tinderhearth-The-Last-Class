using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>
/// 顿帧（命中那一下把战斗冻住几帧）在场景这一侧的外壳，单位是物理帧。
/// </summary>
/// <remarks>
/// 场景每个物理帧先问一次 <see cref="Tick"/>，答「冻结」就这一帧不推进战斗。冻结期间只有战斗
/// 推进停住，相机震动、白闪与打击火花照常走 —— 那正是「打得实」而不是「卡了一下」的条件。
/// </remarks>
public sealed class Hitstop
{
    private readonly HitstopTimer _timer = new();

    /// <summary>还剩几个物理帧的冻结。</summary>
    public int RemainingFrames => _timer.RemainingFrames;

    /// <summary>走一帧外层时钟；返回这一帧是不是冻结帧（递减到零的那一帧仍算冻结）。</summary>
    public bool Tick() => _timer.Tick();

    /// <summary>命中结算做完之后开始冻结，冻 <paramref name="frames"/> 个物理帧。</summary>
    /// <remarks>
    /// 命中当帧调它不会被这一帧吃掉一帧：这一帧的 <see cref="Tick"/> 已经问过了，冻结从下一帧起算。
    /// </remarks>
    public void Begin(int frames) => _timer.Begin(frames);
}
