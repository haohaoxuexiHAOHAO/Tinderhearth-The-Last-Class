namespace Tinderhearth.Rules.Foundation.Actors;

/// <summary>
/// 一个角色此刻站在哪一边。说的是当前的敌我关系，不是族群归属。
/// </summary>
/// <remarks>
/// 这个区分有世界观上的理由：魔化是状态，不是某个族群的天性（设计仓 canon/world/世界观.md 的
/// 「外魔与魔化」一节）。所以一个正在魔化的学生此刻可以是 <see cref="Enemy"/>、被中断之后是
/// <see cref="Ally"/>，而他的族群一天都没变；命名与注释都不该让它读成族群标签。
///
/// 它放在角色这一层而不是放进战斗里，因为用它的不只是战斗里的阻挡：将来的感知系统做敌我判定
/// 时要读同一份（设计仓 canon/gameplay/战斗与关卡.md 的「感知与视野」一节）。各建一套的表现是
/// 两处对敌我的判断悄悄不一致。
///
/// 参照系是玩家那一方，<see cref="Ally"/> 指「与玩家同一边」—— 说的是立场，不是谁在驱动那个
/// 角色，主角与队友都可以由 AI 驱动而仍然是 <see cref="Ally"/>。真正的多方势力（甲乙丙互相
/// 敌对、而且关系会变）需要一张关系矩阵而不是三个值，等有那种关卡再说。
/// </remarks>
public enum CombatSide
{
    /// <summary>与玩家同一边：主角、队友、友方 NPC。</summary>
    Ally,

    /// <summary>此刻与玩家敌对。这是当前关系，不是族群标签，理由见类型说明。</summary>
    Enemy,

    /// <summary>不站边的东西：场景障碍物、可采集资源这一类。谁都挡，谁都不帮。</summary>
    Neutral,
}
