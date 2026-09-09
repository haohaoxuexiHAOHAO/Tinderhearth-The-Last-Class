using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Ui;
using Tinderhearth.UI;

namespace Tinderhearth.Platform;

/// <summary>经输入门面驱动登记的角色，不把本地玩家写死为主角。</summary>
public sealed class LocalPlayerController(string actorId) : ICombatController
{
    public string ActorId { get; } = actorId;
    public InputRouter? Router { get; init; }
    public ActorControllerKind Kind => ActorControllerKind.LocalPlayer;
    public ActorIntent Decide(in ActorView view) => new("idle");

    public CombatInput ReadCombatInput(in ActorView view)
    {
        var router = Router ?? throw new InvalidOperationException("Combat controller requires InputRouter");
        return new(Math.Sign(router.MoveDirection().X), router.IsJustPressed(InputActions.Jump),
            router.IsJustPressed(InputActions.AttackLight), router.IsJustPressed(InputActions.AttackHeavy),
            router.IsJustPressed(InputActions.Dodge), router.IsPressed(InputActions.Sprint));
    }
}
