namespace Tinderhearth.Rules.Combat;

/// <summary>一个人自己的一个回合：还剩多少行动力，那一件占回合的事用掉没有。</summary>
/// <remarks>
/// 规矩在工作区根 desc/战斗规则.md 的「行动力」「占回合的事」与 desc/各类公式计算.md 的「行动力」：回合开始行动力回满；
/// 一回合只有一件占回合的事，做完剩下的行动力减半、向下取整；不做它，可以换成再走满一次；行动力用完、那一件也用掉了，
/// 这一回合就完了。
///
/// 换成再走满一次是剩下的加上满满一笔，所以行动力没用完时也换得了。要是先用完才许换，剩 1 点而下一步贴着敌人要花 2 点时，
/// 用剩下的走不动，又没用完换不了。
///
/// 这里只记账：谁走到哪归 <see cref="Battle"/>，放一招占回合的技能怎么接上这一件归设计仓台账 GP-183。
/// </remarks>
public sealed class Turn
{
    /// <summary>回合开始：行动力回满，那一件还没用。</summary>
    /// <param name="fullActionPoints">每回合回满到多少，要为正。</param>
    public Turn(int fullActionPoints)
    {
        FullActionPoints = fullActionPoints > 0
            ? fullActionPoints
            : throw new ArgumentOutOfRangeException(nameof(fullActionPoints), $"每回合回满的行动力必须为正，实际 {fullActionPoints}");
        ActionPoints = fullActionPoints;
    }

    /// <summary>每回合回满到多少。</summary>
    public int FullActionPoints { get; }

    /// <summary>还剩多少行动力。</summary>
    public int ActionPoints { get; private set; }

    /// <summary>那一件占回合的事用掉了：做了，或者换成了再走满一次。</summary>
    public bool TurnActionUsed { get; private set; }

    /// <summary>这一回合完了：行动力用完，那一件也用掉了。</summary>
    public bool IsOver => TurnActionUsed && ActionPoints == 0;

    /// <summary>把那一件换成再走满一次的话，一共能花多少行动力；那一件已经用掉了是 <c>null</c>。</summary>
    public int? BudgetIfConverted => TurnActionUsed ? null : ActionPoints + FullActionPoints;

    /// <summary>花掉几点行动力，比如走一段路。</summary>
    /// <param name="points">要为正，而且不多于剩下的。</param>
    public void Spend(int points)
    {
        if (points <= 0 || points > ActionPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(points), $"一次要花正数点行动力，最多花剩下的 {ActionPoints} 点，实际 {points}");
        }

        ActionPoints -= points;
    }

    /// <summary>做这一回合那一件占回合的事；做完剩下的行动力减半、向下取整。那一件已经用掉了就被拒，什么都不改。</summary>
    public bool TryTakeTurnAction()
    {
        if (TurnActionUsed)
        {
            return false;
        }

        TurnActionUsed = true;
        ActionPoints /= 2;
        return true;
    }

    /// <summary>把那一件换成再走满一次：剩下的加上满满一笔。那一件已经用掉了就被拒，什么都不改。</summary>
    public bool TryConvertToMove()
    {
        if (TurnActionUsed)
        {
            return false;
        }

        TurnActionUsed = true;
        ActionPoints += FullActionPoints;
        return true;
    }
}
