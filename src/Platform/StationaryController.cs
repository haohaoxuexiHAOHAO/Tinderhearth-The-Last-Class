using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Platform;

/// <summary>不动也不出招的角色控制器：训练房里那个挨打的靶用它。</summary>
/// <remarks>
/// 靶也是个正经角色，而 <see cref="World.PlayerActor"/> 没有控制器会抛，所以得给它一个 ——
/// 但它是被动靶，不该接受任何输入，于是这里每帧回空输入。它挨打之后的反应由命中那条路径经
/// <see cref="World.IHittable"/> 驱动，不靠它自己的输入。
///
/// <c>Kind</c> 取 <c>AI</c>：它不是本地玩家，也不是远程来的，就是一个非玩家驱动的角色。
/// 将来接了敌人 AI，把这个位置换成真的决策控制器就行，训练房那一层不必改。
/// </remarks>
public sealed class StationaryController : ICombatController
{
    public ActorControllerKind Kind => ActorControllerKind.AI;

    public ActorIntent Decide(in ActorView view) => new("idle");

    public CombatInput ReadCombatInput(in ActorView view) => CombatInput.None;
}
