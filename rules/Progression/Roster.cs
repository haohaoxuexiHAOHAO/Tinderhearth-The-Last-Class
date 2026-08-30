namespace Tinderhearth.Rules.Progression;

/// <summary>名册：当前在册的角色。容量**从配置来**，不是代码里的常量（`ENG-5`）。</summary>
public sealed class Roster
{
    private readonly List<string> _actorIds = [];

    public Roster(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "名册容量必须为正");
        }

        Capacity = capacity;
    }

    /// <summary>容量。构造时传入，来自 <c>config/game.json</c>。</summary>
    public int Capacity { get; }

    public IReadOnlyList<string> ActorIds => _actorIds;

    public bool IsFull => _actorIds.Count >= Capacity;

    /// <summary>
    /// 收入一个角色。满了或重复时返回 <c>false</c> 而不抛异常 —— 招募失败是正常玩法结果。
    /// </summary>
    public bool TryAdd(string actorId)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        if (IsFull || _actorIds.Contains(actorId, StringComparer.Ordinal))
        {
            return false;
        }

        _actorIds.Add(actorId);
        return true;
    }

    public bool Contains(string actorId) => _actorIds.Contains(actorId, StringComparer.Ordinal);
}
