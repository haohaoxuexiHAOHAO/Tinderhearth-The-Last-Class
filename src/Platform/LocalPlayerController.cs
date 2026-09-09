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

    /// <summary>把门面的输入压成一帧战斗输入；纵深取移动向量的 Y（`GP-15`）。</summary>
    /// <remarks>
    /// 纵深符号**不用取反**：`move_down` 让 <c>MoveDirection().Y</c> 为正，而
    /// <see cref="DepthBand"/> 的正方向也是向前（靠近镜头、屏幕向下）。两边同向是有意选的，见
    /// <see cref="DepthBand"/> 的坐标口径一节 —— 符号写反不会报错，只会让「往里走」变成「往外走」。
    /// 移动动作四向早在 `UI-7` 就绑好了（W/S 与左摇杆 Y），本条不新增绑定。
    /// </remarks>
    public CombatInput ReadCombatInput(in ActorView view)
    {
        var router = Router ?? throw new InvalidOperationException("Combat controller requires InputRouter");
        var move = router.MoveDirection();
        return new(Math.Sign(move.X), Math.Sign(move.Y), router.IsJustPressed(InputActions.Jump),
            router.IsJustPressed(InputActions.AttackLight), router.IsJustPressed(InputActions.AttackHeavy),
            router.IsJustPressed(InputActions.Dodge), router.IsPressed(InputActions.Sprint));
    }
}
