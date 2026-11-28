using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Combat;

/// <summary>一场仗里上场的一个人：照哪一份角色定义、站在哪一边、此刻站在哪一格。</summary>
/// <remarks>
/// 同一份角色定义可以上场好几个（两个一样的敌人），分得开他们的是 <see cref="Index"/>，不是角色标识。
/// 只有 <see cref="Battle"/> 挪得动他。
/// </remarks>
public sealed class Combatant
{
    internal Combatant(int index, CharacterDefinition character, CombatSide side, GridCell cell)
    {
        Index = index;
        Character = character;
        Side = side;
        Cell = cell;
    }

    /// <summary>他的站位在场景里排第几，从 0 起。敏捷一样时按它排先后。</summary>
    public int Index { get; }

    /// <summary>他照的那一份角色定义。</summary>
    public CharacterDefinition Character { get; }

    /// <summary>他站在哪一边：我方或敌方。</summary>
    public CombatSide Side { get; }

    /// <summary>他此刻站在哪一格。</summary>
    public GridCell Cell { get; internal set; }

    /// <summary>他是不是主角烬：我方阶段永远是烬第一个动。</summary>
    public bool IsProtagonist => Character.Id == CharacterDefinition.ProtagonistId;

    /// <summary>此刻的敏捷，出手顺序按它排。现在只有角色定义上的那一项，宝物、天赋与状态给的等它们落地时加在这里。</summary>
    public int Agility => Character.Attributes.Agility;

    /// <summary>每回合回满到多少行动力：角色定义上的起步值，状态、地形与天赋改它的那一份等它们落地时加在这里。</summary>
    public int FullActionPoints => Character.ActionPoints;

    public override string ToString() => $"{Character.Id}（站位第 {Index + 1} 个，在 {Cell}）";
}
