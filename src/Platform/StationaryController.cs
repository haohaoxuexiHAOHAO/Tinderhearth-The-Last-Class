using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Platform;

/// <summary>不动、不出招的角色控制器：训练房的受击靶用它（`GP-14`）。</summary>
/// <remarks>
/// 训练房把靶从木桩换成真 <see cref="World.PlayerActor"/> 后，那个角色也要有控制器（`PlayerActor`
/// 没控制器会抛）—— 但它是被动靶，不该接受任何输入。所以这里每帧回空输入：它只会站着挨打，受击
/// 反应由命中路径经 <see cref="World.IHittable"/> 驱动，不靠它自己的输入。<c>Kind</c> 取 <c>Ai</c>：
/// 它不是本地玩家，也没有远程来源，就是一个非玩家驱动的角色。将来接了敌人 AI，这个位置换成真的
/// 决策控制器即可，训练房这层不必改（`ENG-5` 的「谁驱动谁由登记决定」）。
/// </remarks>
public sealed class StationaryController : ICombatController
{
    public ActorControllerKind Kind => ActorControllerKind.Ai;

    public ActorIntent Decide(in ActorView view) => new("idle");

    public CombatInput ReadCombatInput(in ActorView view) => CombatInput.None;
}
