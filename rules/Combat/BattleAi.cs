using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Combat;

/// <summary>AI 在一个回合里做的一步。引擎层照这几步一样一样播出来，规则层这时候已经算完了。</summary>
/// <param name="Actor">谁做的。</param>
public abstract record AiStep(Combatant Actor);

/// <summary>走了一段路。</summary>
/// <param name="Actor">谁走的。</param>
/// <param name="Path">走过的那一条路，不含起点、含终点。</param>
public sealed record AiWalk(Combatant Actor, IReadOnlyList<GridCell> Path) : AiStep(Actor);

/// <summary>放了一招。</summary>
/// <param name="Actor">谁放的。</param>
/// <param name="Skill">放的哪一招。</param>
/// <param name="Target">对谁放的。</param>
/// <param name="Use">放成了什么样。</param>
public sealed record AiCast(Combatant Actor, SkillDefinition Skill, Combatant Target, SkillUse Use) : AiStep(Actor);

/// <summary>没人操作的人自己打一个回合：照战斗智慧最低一档。</summary>
/// <remarks>
/// 照工作区根 desc/战斗规则.md 的「战斗智慧」最低一档与「敌人」：先找这一回合打得着的对面的人，敌人先找学生、学生都打不着才找烬，
/// 同一拨里挑走过去花得最少的，走过去用普通攻击打他。这一回合谁都打不着，就朝走过去花得最少的那个对面的人走，能走多远走多远，
/// 然后防御；一步都走不动也照样防御。更高的几档、指令与协作攻击归设计仓台账 GP-36，现在谁都照这一档动。
///
/// 每一步只看局面、照固定的先后挑（花得一样少时先看站位在场景里的先后，再看格子从上到下、从左到右），所以同一局面、
/// 同一串随机数，做的是同一件事。
/// </remarks>
public static class BattleAi
{
    /// <summary>替轮到的那个人打完这一回合，交回他做的那几步。分出胜负了就什么都不做。</summary>
    /// <exception cref="InvalidOperationException">轮到的是这一轮被接管的学生：他这一回合由玩家操作。</exception>
    public static IReadOnlyList<AiStep> PlayTurn(Battle battle)
    {
        ArgumentNullException.ThrowIfNull(battle);
        var steps = new List<AiStep>();
        if (battle.Outcome is not null)
        {
            return steps;
        }

        var me = battle.Current;
        if (me == battle.TakenOver)
        {
            throw new InvalidOperationException($"{me} 这一轮被接管了，轮到他时由玩家操作");
        }

        var thisTurn = battle.MapOfCurrent(battle.ActionPoints);
        var attacked = false;
        if (CanUse(battle, me.BasicAttack) && FirstInReach(battle, me, thisTurn) is { } plan)
        {
            WalkTo(battle, plan.Cell, steps);
            attacked = TryCast(battle, me.BasicAttack, plan.Target, steps);
        }
        else if (Nearest(battle, me, battle.MapOfCurrent(int.MaxValue)) is { } goal)
        {
            WalkToward(battle, goal, thisTurn, steps);
        }

        if (!attacked && StillMyTurn(battle, me))
        {
            _ = TryCast(battle, me.Defend, me, steps);
        }

        if (StillMyTurn(battle, me))
        {
            battle.EndTurn();
        }

        return steps;
    }

    /// <summary>站到哪一格去打谁、走到那一格花多少。</summary>
    private sealed record Spot(Combatant Target, GridCell Cell, int Cost, IReadOnlyList<GridCell>? Path);

    /// <summary>这一回合打得着的那一个：敌人先找学生、学生都打不着才找烬；同一拨里挑走过去花得最少的。</summary>
    private static Spot? FirstInReach(Battle battle, Combatant me, MovementMap thisTurn) =>
        TargetGroups(battle, me)
            .Select(group => Cheapest(group.Select(target => BestSpot(me, target, thisTurn))))
            .FirstOrDefault(spot => spot is not null);

    /// <summary>走过去花得最少的那个对面的人，不分学生与烬；一个都走不到是 <c>null</c>。</summary>
    private static Spot? Nearest(Battle battle, Combatant me, MovementMap anywhere) =>
        Cheapest(TargetGroups(battle, me).SelectMany(group => group).Select(target => BestSpot(me, target, anywhere)));

    /// <summary>对面站着的人分几拨、按先找哪一拨排：敌人先学生后烬（desc 的「敌人」：敌人优先攻击学生），我方只有一拨。</summary>
    private static IEnumerable<IEnumerable<Combatant>> TargetGroups(Battle battle, Combatant me)
    {
        var opposite = battle.Combatants.Where(c => c.Side != me.Side && !c.IsDown).ToList();
        if (me.Side == CombatSide.Enemy)
        {
            yield return opposite.Where(c => !c.IsProtagonist);
            yield return opposite.Where(c => c.IsProtagonist);
        }
        else
        {
            yield return opposite;
        }
    }

    /// <summary>打这个人要站的那一格：他在普通攻击的施放距离以内、这张图上走得到的格子里花得最少的；站着就打得着是原地那一格。</summary>
    private static Spot? BestSpot(Combatant me, Combatant target, MovementMap map)
    {
        var range = me.BasicAttack.Range;
        if (GridDistance.Between(me.Cell, target.Cell) <= range)
        {
            return new Spot(target, me.Cell, 0, null);
        }

        return map.Reachable
            .Where(c => GridDistance.Between(c.Key, target.Cell) <= range)
            .OrderBy(c => c.Value)
            .ThenBy(c => c.Key.Y)
            .ThenBy(c => c.Key.X)
            .Select(c => new Spot(target, c.Key, c.Value, map.PathTo(c.Key)))
            .FirstOrDefault();
    }

    private static Spot? Cheapest(IEnumerable<Spot?> spots) =>
        spots.OfType<Spot>().OrderBy(s => s.Cost).ThenBy(s => s.Target.Index).FirstOrDefault();

    /// <summary>沿走到那一格的那条路，走到这一回合走得到、停得下的最远一格。</summary>
    private static void WalkToward(Battle battle, Spot goal, MovementMap thisTurn, List<AiStep> steps)
    {
        if (goal.Path is not { } path)
        {
            return;
        }

        // 路上每一格花得越来越多，这一回合走得到的是前面那一段；里面站着同伴的那几格只许穿过去，停不下。
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (thisTurn.Reachable.ContainsKey(path[i]))
            {
                WalkTo(battle, path[i], steps);
                return;
            }
        }
    }

    private static void WalkTo(Battle battle, GridCell cell, List<AiStep> steps)
    {
        var me = battle.Current;
        if (cell != me.Cell && battle.MoveTo(cell) is { } path)
        {
            steps.Add(new AiWalk(me, path));
        }
    }

    /// <summary>放得出来就放，记下这一步。</summary>
    private static bool TryCast(Battle battle, SkillDefinition skill, Combatant target, List<AiStep> steps)
    {
        var me = battle.Current;
        if (battle.RefusalFor(skill, target) is not null)
        {
            return false;
        }

        steps.Add(new AiCast(me, skill, target, battle.UseSkill(skill, target)));
        return true;
    }

    /// <summary>这一招这一回合还用不用得了，不看对谁放：没在冷却，占回合的话那一件也还没用掉。</summary>
    private static bool CanUse(Battle battle, SkillDefinition skill) =>
        battle.Current.CooldownLeft(skill) == 0 && !(skill.TakesTurn && battle.TurnActionUsed);

    private static bool StillMyTurn(Battle battle, Combatant me) => battle.Outcome is null && battle.Current == me;
}
