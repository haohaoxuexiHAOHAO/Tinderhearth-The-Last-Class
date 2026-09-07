namespace Tinderhearth.Rules.Combat;

/// <summary>A1 的临时状态种类。</summary>
public enum StatusKind
{
    /// <summary>受击硬直。</summary>
    Hitstun,
    /// <summary>无敌。</summary>
    Invulnerable,
}

/// <summary>状态的只读快照；剩余帧为零表示未生效。</summary>
public readonly record struct StatusEffect(StatusKind Kind, int RemainingFrames, bool Removable);

/// <summary>硬直与无敌的统一载体；同种刷新取新时长，不叠加。</summary>
/// <remarks>
/// Apply 后立即生效，经过 N 次 Tick 后移除。拥有者在每个非顿帧逻辑帧开头推进一次，再施加本帧新状态。
/// MotorState 内的载体只由 MotorState.Tick 推进，调用方不得重复推进；独立载体由角色逻辑拥有者推进。
/// A1 两种状态均不可主动解除，零时长也不能作为解除通道。
/// </remarks>
public sealed class StatusEffects
{
    private readonly int[] _remaining = new int[2];

    /// <summary>读取快照，不暴露内部可变存储。</summary>
    public StatusEffect Get(StatusKind kind) => new(kind, _remaining[Index(kind)], false);

    /// <summary>该种状态当前是否生效。</summary>
    public bool Has(StatusKind kind) => Get(kind).RemainingFrames > 0;

    /// <summary>施加正整数帧时长；非法输入抛出异常且不修改已有状态。</summary>
    public void Apply(StatusKind kind, int frames)
    {
        var index = Index(kind);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        _remaining[index] = frames;
    }

    /// <summary>尝试主动解除；A1 两种状态都拒绝，且不影响剩余时间。</summary>
    public bool TryRemove(StatusKind kind)
    {
        _ = Index(kind);
        return false;
    }

    /// <summary>推进一次逻辑帧，所有状态递减并在零帧失效，不下溢。</summary>
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知状态种类"),
    };
}
