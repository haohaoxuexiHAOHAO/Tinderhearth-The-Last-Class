using Godot;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.Platform;

/// <summary>本地玩家的控制器：从输入门面读按键，驱动它被指派到的那个角色。</summary>
/// <remarks>
/// 它拿的是一个角色标识，不是写死的主角 —— 谁被玩家驱动由控制器登记表决定。
/// </remarks>
public sealed class LocalPlayerController(string actorId) : ICombatController
{
    public string ActorId { get; } = actorId;
    public InputRouter? Router { get; init; }
    public ActorControllerKind Kind => ActorControllerKind.LocalPlayer;
    public ActorIntent Decide(in ActorView view) => new("idle");

    /// <summary>把门面读到的输入压成一帧战斗输入。纵深取移动向量的 Y。</summary>
    /// <remarks>
    /// 纵深符号不用取反：按下后退让 <c>MoveDirection().Y</c> 为正，而 <see cref="DepthBand"/> 的
    /// 正方向也是向前（靠近镜头、屏幕向下），两边本来就同向。符号写反不报错，只会让「往里走」
    /// 变成「往外走」。
    /// </remarks>
    public CombatInput ReadCombatInput(in ActorView view)
    {
        var router = Router ?? throw new InvalidOperationException("本地玩家控制器没拿到输入门面，读不到玩家按了什么 —— 建它的时候要把门面传进来");
        Vector2 move = router.MoveDirection();
        return new(
            HorizontalSign: Math.Sign(move.X),
            DepthSign: Math.Sign(move.Y),
            JumpPressed: router.IsJustPressed(InputActions.Jump),
            LightPressed: router.IsJustPressed(InputActions.AttackLight),
            HeavyPressed: router.IsJustPressed(InputActions.AttackHeavy),
            DodgePressed: router.IsJustPressed(InputActions.Dodge),
            RunHeld: router.IsPressed(InputActions.Run));
    }
}
