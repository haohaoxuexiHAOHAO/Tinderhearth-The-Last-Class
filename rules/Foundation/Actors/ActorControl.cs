namespace Tinderhearth.Rules.Foundation.Actors;

/// <summary>谁在驱动这个角色。</summary>
/// <remarks>
/// <c>Remote</c> 现在没有实现，**但枚举值先占位**：联机尚未立项，玩法正典写明届时玩家使用
/// 自定义角色、不扮演主角也不扮演学员。留这个值的成本是一行，事后补要翻遍调用点（`ENG-5`）。
/// </remarks>
public enum ActorControllerKind
{
    LocalPlayer,
    Ai,
    Remote,
}

/// <summary>规则层看到的角色现状快照。</summary>
/// <remarks>
/// 字段刻意只有身份：**具体要看到什么由战斗与经营系统的实现需求决定**，玩法数值仍未设计
/// （`GP-2`），现在往里塞字段就是凭手感猜。这个类型会长大，但它的位置不会变。
/// </remarks>
/// <param name="ActorId">角色的稳定标识，与角色定义数据里的 <c>id</c> 一致。</param>
public readonly record struct ActorView(string ActorId);

/// <summary>角色这一步想做什么。</summary>
/// <param name="Action">意图的名字。取值范围归战斗与经营系统，规则层不预设。</param>
/// <param name="TargetActorId">意图的目标角色，没有目标时为 <c>null</c>。</param>
public readonly record struct ActorIntent(string Action, string? TargetActorId = null);

/// <summary>
/// 决定一个角色下一步做什么。本地玩家、AI、将来的远程玩家都实现这一个接口（`ENG-5`）。
/// </summary>
/// <remarks>
/// **这条预留真正要防的是「把玩家写死成主角」。** 主角只是恰好通常由 <c>LocalPlayer</c>
/// 驱动的一个角色，不是「玩家」的同义词 —— 玩法正典已经写明联机时玩家用自定义角色。
/// 所以任何「这个角色是不是主角」的判断都不得用来决定它由谁驱动，反之亦然。
/// </remarks>
public interface IActorController
{
    ActorControllerKind Kind { get; }

    ActorIntent Decide(in ActorView view);
}

/// <summary>
/// 角色标识 → 控制器。谁驱动谁在这里查，代码里不出现「主角固定由玩家控制」这种硬编码。
/// </summary>
public sealed class ActorControllerRegistry
{
    private readonly Dictionary<string, IActorController> _byActorId = new(StringComparer.Ordinal);

    /// <summary>登记或替换某个角色的控制器。可替换是本类存在的全部理由。</summary>
    public void Assign(string actorId, IActorController controller)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentNullException.ThrowIfNull(controller);
        _byActorId[actorId] = controller;
    }

    public bool TryGet(string actorId, out IActorController? controller) =>
        _byActorId.TryGetValue(actorId, out controller);

    /// <summary>取控制器。没登记就抛 —— 一个没人驱动的角色会一动不动，静默放过更难查。</summary>
    public IActorController Require(string actorId) =>
        _byActorId.TryGetValue(actorId, out var controller)
            ? controller
            : throw new KeyNotFoundException($"角色 {actorId} 没有登记控制器");

    public int Count => _byActorId.Count;
}
