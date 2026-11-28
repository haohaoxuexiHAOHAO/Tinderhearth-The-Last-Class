using System.Diagnostics.CodeAnalysis;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;

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
/// 引擎层只把点击翻成这里的命令、把结果画出来。走不通、放不出来的命令什么都不改，不抛，理由同 <see cref="Plot"/>：
/// 点了一格走不到、点了一个够不着的人，是玩家碰得到的正常结果。命中与暴击从开打时给的随机源掷，同一局面、同一串随机数，
/// 打出来的每一步都一样。
/// </remarks>
public sealed class Battle
{
    private readonly CombatContent _content;
    private readonly Func<double> _roll;
    private IReadOnlyList<Combatant> _order;
    private int _turnIndex;
    private Turn _turn;

    private Battle(BattleRoom room, CombatContent content, Func<double> roll, IReadOnlyList<Combatant> combatants)
    {
        Room = room;
        _content = content;
        _roll = roll;
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
    /// <param name="roll">掷一次骰：给一个 [0, 1) 里的数，小于几率就算中。命中与暴击都从这里掷。</param>
    public static Battle Start(BattleRoom room, CombatContent content, Func<double> roll)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(roll);

        var missing = room.Positions.Select(p => p.CharacterId).Where(id => !content.Characters.ContainsKey(id)).Distinct().ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"这一间屋的站位指着 {string.Join("、", missing)}，打仗那份内容里没有：屋子要用同一份内容里的角色核", nameof(room));
        }

        Combatant[] combatants = [.. room.Positions.Select((p, index) => Enter(index, p, content))];
        return new Battle(room, content, roll, combatants);
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
        EndTurnIfOver();
        return path;
    }

    /// <summary>轮到的那个人这时候对这个人放这一招，放不放得出来。</summary>
    /// <param name="skill">放哪一招，要是他自己会的。</param>
    /// <param name="target">对谁放：指向对面一个人的招点那个人，对自己放的招点他自己。</param>
    /// <returns>放得出来是 <c>null</c>，放不出来是为什么。施放距离照走路的数法，不管中间隔着墙还是人，这一份不看视野。</returns>
    public SkillRefusal? RefusalFor(SkillDefinition skill, Combatant target)
    {
        RequireUsable(skill, target);
        var user = Current;
        // 占回合的那一件用掉了排在冷却前面：它拦的是这一回合所有占回合的招，比哪一招在冷却更说得清为什么放不了。
        return skill switch
        {
            { TakesTurn: true } when _turn.TurnActionUsed => SkillRefusal.TurnActionUsed,
            _ when user.CooldownLeft(skill) > 0 => SkillRefusal.OnCooldown,
            { Target: SkillTarget.Self } when target != user => SkillRefusal.WrongTarget,
            { Target: SkillTarget.Enemy } when target.Side == user.Side => SkillRefusal.WrongTarget,
            _ when GridDistance.Between(user.Cell, target.Cell) > skill.Range => SkillRefusal.OutOfRange,
            _ => null,
        };
    }

    /// <summary>瞄准时写的数：轮到的那个人用这一招打这个人，打中的几率与打多少。</summary>
    /// <returns>这一招不打人、或者点的是自己这一边的人时是 <c>null</c>。够不够得着不在这里判，看 <see cref="RefusalFor"/>。</returns>
    public StrikeOdds? Preview(SkillDefinition skill, Combatant target)
    {
        RequireUsable(skill, target);
        return skill.DamageKind is null || target.Side == Current.Side ? null : OddsAgainst(target, skill);
    }

    /// <summary>轮到的那个人对这个人放这一招。</summary>
    /// <remarks>
    /// 占回合的招用掉这一回合那一件占回合的事，剩下的行动力减半；打人的招先掷命中，打中了再掷暴击；给自己挂的状态挂上。
    /// 放完行动力用完、那一件也用掉了，这一回合当场结束，轮到下一个。放不出来的什么都不改。
    /// </remarks>
    public SkillUse UseSkill(SkillDefinition skill, Combatant target)
    {
        if (RefusalFor(skill, target) is { } refusal)
        {
            return new SkillUse(refusal, null);
        }

        var user = Current;
        if (skill.TakesTurn)
        {
            // 那一件还没用掉，上面已经判过，所以这一下做得成。
            _ = _turn.TryTakeTurnAction();
        }

        user.MarkUsed(skill);
        Strike? strike = skill.DamageKind is null ? null : Resolve(target, skill);
        if (skill.SelfStatusId is { } status)
        {
            user.AddStatus(status);
        }

        EndTurnIfOver();
        return new SkillUse(null, strike);
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

    /// <summary>照站位上场：面板照属性算一次，会的招从内容里取。内容核过角色指的招都在册。</summary>
    private static Combatant Enter(int index, StartingPosition position, CombatContent content)
    {
        var character = content.Characters[position.CharacterId];
        return new Combatant(
            index: index,
            character: character,
            side: position.Side,
            cell: position.Cell,
            panel: content.PanelOf(character),
            basicAttack: content.Skills[character.BasicAttackSkillId],
            defend: content.Skills[character.DefendSkillId]);
    }

    /// <summary>放的是轮到的那个人会的招、对的是这一场里的人；不是就是调用方的错，当场抛。</summary>
    private void RequireUsable(SkillDefinition skill, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(target);
        if (!Current.Skills.Contains(skill))
        {
            throw new ArgumentException($"{Current} 不会「{skill.Id}」：只能放他自己会的招", nameof(skill));
        }

        if (!Combatants.Contains(target))
        {
            throw new ArgumentException($"{target} 不在这一场里", nameof(target));
        }
    }

    private StrikeOdds OddsAgainst(Combatant target, SkillDefinition skill) => StrikeOdds.Compute(
        attacker: Current.Panel,
        defender: target.Panel,
        defenderBlocking: target.Statuses.Contains(CombatStatusIds.Block),
        skill: skill,
        distance: GridDistance.Between(Current.Cell, target.Cell),
        config: _content.Config);

    /// <summary>真打一下：先掷命中，打中了再掷暴击，打空不掷暴击。打多少取瞄准时那一份算出来的数。</summary>
    private Strike Resolve(Combatant target, SkillDefinition skill)
    {
        StrikeOdds odds = OddsAgainst(target, skill);
        if (Roll() >= odds.HitChance)
        {
            return new Strike(Hit: false, Critical: false, Damage: 0);
        }

        var critical = Roll() < odds.CriticalChance;
        var damage = critical ? odds.CriticalDamage : odds.Damage;
        target.TakeDamage(damage);
        return new Strike(Hit: true, Critical: critical, Damage: damage);
    }

    /// <summary>掷一次骰。给的数不在 [0, 1) 里就是随机源接错了，那样会悄悄变成总打不中或总打得中，所以当场抛。</summary>
    private double Roll()
    {
        var value = _roll();
        return value is >= 0 and < 1
            ? value
            : throw new InvalidOperationException($"随机源要给 [0, 1) 里的数，给的是 {value}");
    }

    private void EndTurnIfOver()
    {
        if (_turn.IsOver)
        {
            EndTurn();
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

    /// <summary>轮到的那个人的回合开始：行动力回满，上一回合没用完的不留；他挂的「格挡」解除。</summary>
    [MemberNotNull(nameof(_turn))]
    private void BeginTurn()
    {
        _turn = new Turn(Current.FullActionPoints);
        Current.BeginOwnTurn();
    }

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
