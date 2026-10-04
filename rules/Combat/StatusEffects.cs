namespace Tinderhearth.Rules.Combat;
/// <summary>临时状态的种类。</summary>
public enum StatusKind
{
    /// <summary>受击硬直。</summary>
    Hitstun,
    /// <summary>无敌。</summary>
    Invulnerable,
}
/// <summary>一种状态的只读快照。剩余帧数为零表示它没在生效。</summary>
public readonly record struct StatusEffect(StatusKind Kind, int RemainingFrames, bool Removable);
/// <summary>硬直与无敌的统一载体。同一种状态再施加一次取新时长，不叠加。</summary>
/// <remarks>
/// <see cref="Apply"/> 之后立即生效，再经过那么多次 <see cref="Tick"/> 移除。拥有者在每个非顿帧
/// 逻辑帧开头推进一次，然后才施加本帧的新状态。
///
/// <see cref="MotorState"/> 里那个载体只由 <c>MotorState.Tick</c> 推进，调用方不得再推进一次；
/// 独立的载体由持有它的角色逻辑推进。两种状态都不可主动解除，施加零时长也不是解除的通道。
/// </remarks>
public sealed class StatusEffects
{
    private readonly int[] _remaining = new int[2];
    /// <summary>读一份快照出来，不暴露内部那个可变数组。</summary>
    public StatusEffect Get(StatusKind kind) => new(kind, _remaining[Index(kind)], false);
    /// <summary>这一种状态当前生效吗。</summary>
    public bool Has(StatusKind kind) => Get(kind).RemainingFrames > 0;
    /// <summary>施加正整数帧的时长。非法输入抛出，且不改动已有状态。</summary>
    public void Apply(StatusKind kind, int frames)
    {
        var index = Index(kind);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        _remaining[index] = frames;
    }
    /// <summary>尝试主动解除。两种状态都拒绝，而且不影响剩余时间。</summary>
    public bool TryRemove(StatusKind kind)
    {
        _ = Index(kind);
        return false;
    }
    /// <summary>推进一个逻辑帧：所有状态各减一帧，减到零就失效，不会减成负数。</summary>
    public void Tick()
    {
        for (var i = 0; i < _remaining.Length; i++)
        {
            if (_remaining[i] > 0)
            {
                _remaining[i]--;
            }
        }
    }
    private static int Index(StatusKind kind) => kind switch
    {
        StatusKind.Hitstun => 0,
        StatusKind.Invulnerable => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的状态种类"),
    };
}
