using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Rules.Combat;

/// <summary>控制器的战斗帧能力；身份登记仍通过 IActorController。</summary>
public interface ICombatController : IActorController
{
    CombatInput ReadCombatInput(in ActorView view);
}
