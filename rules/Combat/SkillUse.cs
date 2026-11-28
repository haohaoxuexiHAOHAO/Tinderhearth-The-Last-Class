namespace Tinderhearth.Rules.Combat;

/// <summary>一招放不出来的原因。被拒的那一下什么都不改：行动力、那一件占回合的事、冷却与谁的 HP 都不动，也不掷骰。</summary>
public enum SkillRefusal
{
    /// <summary>这一场已经分出胜负，不再有人行动。</summary>
    BattleOver,

    /// <summary>还在冷却。</summary>
    OnCooldown,

    /// <summary>这一招占回合，这一回合那一件占回合的事已经用掉了：做过了，或者换成了再走满一次。</summary>
    TurnActionUsed,

    /// <summary>对象不对：指向对面一个人的招点了自己这一边的人，对自己放的招点了别人，或者点了倒下、死了的人。</summary>
    WrongTarget,

    /// <summary>目标不在施放距离以内。</summary>
    OutOfRange,
}

/// <summary>一招打一个人打成了什么样。</summary>
/// <param name="Hit">打中没有。</param>
/// <param name="Critical">暴击没有。打空不掷暴击，所以打空时总是 <c>false</c>。</param>
/// <param name="Damage">打了多少，打空是 0。</param>
public readonly record struct Strike(bool Hit, bool Critical, int Damage);

/// <summary>放一招的结果。</summary>
/// <param name="Refusal">放不出来的原因；放出去了是 <c>null</c>。</param>
/// <param name="Strike">打人的招打成了什么样；被拒了或者这一招不打人，是 <c>null</c>。</param>
public readonly record struct SkillUse(SkillRefusal? Refusal, Strike? Strike)
{
    /// <summary>放出去了没有。</summary>
    public bool Used => Refusal is null;
}
