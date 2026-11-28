using System.Diagnostics.CodeAnalysis;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Rules.Combat;

/// <summary>一轮里的两个阶段：先我方，后敌方。</summary>
public enum BattlePhase
{
    /// <summary>我方阶段：烬第一个动，学生按敏捷从高到低。</summary>
    Ally,

    /// <summary>敌方阶段：敌人按敏捷从高到低。</summary>
    Enemy,
}

/// <summary>一场仗打到哪了：谁站在哪一格，这一轮谁先谁后，轮到谁，他这一回合还剩多少行动力。</summary>
/// <remarks>
/// 规矩在工作区根 desc/战斗规则.md 的「轮与回合」：一轮先我方阶段、后敌方阶段，每人轮流过一个自己的回合。出手顺序在每轮开始
/// 按当时的敏捷排一次，这一轮里不再动；敏捷一样时按站位在场景里的先后，不掷骰。
///
/// 引擎层只把点击翻成这里的命令、把结果画出来。走不通的命令返回 <c>null</c>、什么都不改，不抛，理由同 <see cref="Plot"/>：
/// 点了一格走不到，是玩家碰得到的正常结果。
/// </remarks>
public sealed class Battle
{
    private readonly CombatContent _content;
    private IReadOnlyList<Combatant> _order;
    private int _turnIndex;
    private Turn _turn;

    private Battle(BattleRoom room, CombatContent content, IReadOnlyList<Combatant> combatants)
    {
        Room = room;
        _content = content;
        Combatants = combatants;
        StartRound();
    }

    /// <summary>打的那一间屋。</summary>
    public BattleRoom Room { get; }

    /// <summary>上场的每一个人，照站位在场景里的先后。</summary>
    public IReadOnlyList<Combatant> Combatants { get; }

    /// <summary>第几轮，从 1 起。</summary>
    public int Round { get; private set; }

    /// <summary>这一轮的出手顺序：我方阶段的在前，敌方阶段的在后。</summary>
    public IReadOnlyList<Combatant> Order => _order;

    /// <summary>轮到谁。</summary>
    public Combatant Current => _order[_turnIndex];

    /// <summary>现在是哪一个阶段。</summary>
    public BattlePhase Phase => Current.Side == CombatSide.Ally ? BattlePhase.Ally : BattlePhase.Enemy;

    /// <summary>轮到的那个人还剩多少行动力。</summary>
    public int ActionPoints => _turn.ActionPoints;

    /// <summary>轮到的那个人这一回合那一件占回合的事用掉了没有：做了，或者换成了再走满一次。</summary>
    public bool TurnActionUsed => _turn.TurnActionUsed;

    /// <summary>照那一间屋的站位开打：第一轮排好顺序，轮到烬。</summary>
    /// <param name="room">那一间屋，要用同一份内容里的角色核过。</param>
    /// <param name="content">打仗要读的角色、技能与配置。</param>
    public static Battle Start(BattleRoom room, CombatContent content)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(content);

        var missing = room.Positions.Select(p => p.CharacterId).Where(id => !content.Characters.ContainsKey(id)).Distinct().ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"这一间屋的站位指着 {string.Join("、", missing)}，打仗那份内容里没有：屋子要用同一份内容里的角色核", nameof(room));
        }

        Combatant[] combatants =
            [.. room.Positions.Select((p, index) => new Combatant(index, content.Characters[p.CharacterId], p.Side, p.Cell))];
        return new Battle(room, content, combatants);
    }

    /// <summary>轮到的那个人此刻能走到哪。</summary>
    public MoveReach Reach()
    {
        var standing = Combatants.ToDictionary(c => c.Cell, c => c.Side);
        MovementConfig costs = _content.Config.Movement;
        var withRemaining = MovementMap.Compute(Room, standing, Current.Cell, Current.Side, _turn.ActionPoints, costs);
        MovementMap? ifConverted = _turn.BudgetIfConverted is { } budget
            ? MovementMap.Compute(Room, standing, Current.Cell, Current.Side, budget, costs)
            : null;
        return new MoveReach(withRemaining, ifConverted);
    }

    /// <summary>点一格：走得到就沿那一条路走过去，扣掉花的行动力；要把那一件换成再走满一次才走得到的，换了再走。</summary>
    /// <returns>走过的那一条路，不含起点、含终点；走不到、停不下的是 <c>null</c>，人不动、行动力不扣。</returns>
    /// <remarks>走完行动力用完、那一件也用掉了，这一回合当场结束，轮到下一个。</remarks>
    public IReadOnlyList<GridCell>? MoveTo(GridCell cell)
    {
        MoveReach reach = Reach();
        if (reach.PathTo(cell) is not { } path || reach.CostTo(cell) is not { } cost)
        {
            return null;
        }

        if (reach.OnlyByConverting.Contains(cell))
        {
            // 换了才走得到的格子只在那一件还没用时才算得出来，所以这一下换得成。
            _ = _turn.TryConvertToMove();
        }

        _turn.Spend(cost);
        Current.Cell = cell;
        if (_turn.IsOver)
        {
            EndTurn();
        }

        return path;
    }

    /// <summary>结束这一回合，剩下的行动力作废；轮到下一个，这一轮的人都过完了就开始下一轮。</summary>
    public void EndTurn()
    {
        if (_turnIndex + 1 < _order.Count)
        {
            _turnIndex++;
            BeginTurn();
        }
        else
        {
            StartRound();
        }
    }

    /// <summary>一轮开始：按此刻的敏捷排一次出手顺序，轮到第一个。</summary>
    [MemberNotNull(nameof(_order), nameof(_turn))]
    private void StartRound()
    {
        Round++;
        _order = OrderOf(Combatants);
        _turnIndex = 0;
        BeginTurn();
    }

    /// <summary>轮到的那个人的回合开始：行动力回满，上一回合没用完的不留。</summary>
    [MemberNotNull(nameof(_turn))]
    private void BeginTurn() => _turn = new Turn(Current.FullActionPoints);

    /// <summary>我方烬第一个、学生按敏捷从高到低，敌方按敏捷从高到低；敏捷一样时按站位在场景里的先后。</summary>
    private static IReadOnlyList<Combatant> OrderOf(IReadOnlyList<Combatant> combatants) =>
    [
        .. combatants
            .Where(c => c.Side == CombatSide.Ally)
            .OrderByDescending(c => c.IsProtagonist)
            .ThenByDescending(c => c.Agility)
            .ThenBy(c => c.Index),
        .. combatants
            .Where(c => c.Side == CombatSide.Enemy)
            .OrderByDescending(c => c.Agility)
            .ThenBy(c => c.Index),
    ];
}
