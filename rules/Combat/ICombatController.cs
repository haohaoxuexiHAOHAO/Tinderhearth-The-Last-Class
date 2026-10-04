using Tinderhearth.Rules.Foundation.Actors;
namespace Tinderhearth.Rules.Combat;
/// <summary>能给出战斗输入的控制器。身份登记仍走 <see cref="IActorController"/>。</summary>
public interface ICombatController : IActorController
{
    /// <summary>读出这一帧的战斗输入。<paramref name="view"/> 是控制器看得到的那部分角色现状。</summary>
    CombatInput ReadCombatInput(in ActorView view);
}
