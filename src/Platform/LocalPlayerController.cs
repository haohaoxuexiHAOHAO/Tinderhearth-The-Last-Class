using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Platform;

/// <summary>
/// 由本机玩家的输入驱动一个角色。
/// </summary>
/// <remarks>
/// **它绑定的是「某个角色」，不是「主角」**（`ENG-5`）。构造时传入 actorId，谁被玩家驱动
/// 由登记表决定 —— 联机若做，远程玩家的实现会与它并列，而不是在它内部加分支。
///
/// 现在只回一个空转意图：按键结构、连段与取消窗口归战斗系统，玩法数值仍未设计（`GP-2`），
/// 在这里映射具体按键就是凭手感猜。这个类型存在的意义是把接缝先钉住。
/// </remarks>
public sealed class LocalPlayerController(string actorId) : IActorController
{
    public string ActorId { get; } = actorId;

    public ActorControllerKind Kind => ActorControllerKind.LocalPlayer;

    public ActorIntent Decide(in ActorView view) => new("idle");
}
