using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Combat;

/// <summary>一场仗里上场的一个人：照哪一份角色定义、站在哪一边、站在哪一格，还剩多少 HP、挂着什么、哪几招在冷却。</summary>
/// <remarks>
/// 同一份角色定义可以上场好几个（两个一样的敌人），分得开他们的是 <see cref="Index"/>，不是角色标识。
/// 只有 <see cref="Battle"/> 改得动他。
///
/// 冷却按他自己的回合数算（工作区根 desc/战斗规则.md 的「冷却与额外回合」）：一招在他第 T 个回合用过、冷却 N，
/// 到他第 T + N 个回合才能再用，别人的回合不算。
/// </remarks>
public sealed class Combatant
{
    private readonly HashSet<string> _statuses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _usedOnOwnTurn = new(StringComparer.Ordinal);

    internal Combatant(
        int index,
        CharacterDefinition character,
        CombatSide side,
        GridCell cell,
        CombatPanel panel,
        SkillDefinition basicAttack,
        SkillDefinition defend)
    {
        Index = index;
        Character = character;
        Side = side;
        Cell = cell;
        Panel = panel;
        Hp = panel.HpMax;
        BasicAttack = basicAttack;
        Defend = defend;
        Skills = [basicAttack, defend];
    }

    /// <summary>他的站位在场景里排第几，从 0 起。敏捷一样时按它排先后。</summary>
    public int Index { get; }

    /// <summary>他照的那一份角色定义。</summary>
    public CharacterDefinition Character { get; }

    /// <summary>他站在哪一边：我方或敌方。</summary>
    public CombatSide Side { get; }

    /// <summary>他此刻站在哪一格。</summary>
    public GridCell Cell { get; internal set; }

    /// <summary>他的面板，开打时照他的属性算一次。</summary>
    public CombatPanel Panel { get; }

    /// <summary>还剩多少 HP，最少 0。</summary>
    public int Hp { get; private set; }

    /// <summary>HP 到 0 了：我方的是倒下，敌方的是死亡。倒下、死了的不再行动，也选不中。</summary>
    /// <remarks>选不中是照「神秘庇佑」那一条（工作区根 desc/状态.md）；那个状态本身这一场还没有，扶起也还没有。</remarks>
    public bool IsDown => Hp == 0;

    /// <summary>还占着那一格：站着的人与倒下的我方；死了的敌人从格子上拿掉，那一格谁都走得进去。</summary>
    public bool OccupiesCell => !IsDown || Side == CombatSide.Ally;

    /// <summary>他挂着的状态，按标识记；认得哪几个在 <see cref="CombatStatusIds"/>。</summary>
    public IReadOnlySet<string> Statuses => _statuses;

    /// <summary>他的普通攻击。</summary>
    public SkillDefinition BasicAttack { get; }

    /// <summary>他的防御。</summary>
    public SkillDefinition Defend { get; }

    /// <summary>他会的招，照这个先后摆：普通攻击、防御。</summary>
    public IReadOnlyList<SkillDefinition> Skills { get; }

    /// <summary>他过到第几个自己的回合了，从 1 起；还没轮到过他是 0。</summary>
    public int OwnTurns { get; private set; }

    /// <summary>他是不是主角烬：我方阶段永远是烬第一个动。</summary>
    public bool IsProtagonist => Character.Id == CharacterDefinition.ProtagonistId;

    /// <summary>此刻的敏捷，出手顺序按它排。现在只有角色定义上的那一项，宝物、天赋与状态给的等它们落地时加在这里。</summary>
    public int Agility => Character.Attributes.Agility;

    /// <summary>每回合回满到多少行动力：角色定义上的起步值，状态、地形与天赋改它的那一份等它们落地时加在这里。</summary>
    public int FullActionPoints => Character.ActionPoints;

    /// <summary>这一招还要再过几个他自己的回合才能用；0 就是现在能用。</summary>
    public int CooldownLeft(SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return _usedOnOwnTurn.TryGetValue(skill.Id, out var usedOn) ? Math.Max(0, usedOn + skill.Cooldown - OwnTurns) : 0;
    }

    public override string ToString() => $"{Character.Id}（站位第 {Index + 1} 个，在 {Cell}）";

    /// <summary>他自己的回合开始：回合数加一，「格挡」解除 —— 它到自己下一回合为止（工作区根 desc/状态.md 的「持续多久」）。</summary>
    internal void BeginOwnTurn()
    {
        OwnTurns++;
        _statuses.Remove(CombatStatusIds.Block);
    }

    /// <summary>这一招在他这一回合用过了，冷却从这一回合算起。</summary>
    internal void MarkUsed(SkillDefinition skill) => _usedOnOwnTurn[skill.Id] = OwnTurns;

    /// <summary>挂上一个状态；已经挂着就照旧，只有一层。</summary>
    internal void AddStatus(string statusId) => _statuses.Add(statusId);

    /// <summary>受到这么多伤害，HP 扣到 0 为止。</summary>
    internal void TakeDamage(int damage) => Hp = Math.Max(0, Hp - damage);
}
