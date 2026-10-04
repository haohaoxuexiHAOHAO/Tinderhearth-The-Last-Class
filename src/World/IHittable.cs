using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>能挨打的目标。命中结算把反应交给目标自己处理，攻击方不必知道对面是木桩还是角色。</summary>
/// <remarks>
/// 本接口只表达「挨打之后怎么反应」。命中要不要成立（横向重叠加纵深容差）仍在
/// <see cref="Hitbox"/> 里判，与这里无关。
/// </remarks>
public interface IHittable
{
    /// <summary>接受一次命中反应；击退方向由攻击方朝向 <paramref name="facing"/> 给出（−1 左 / +1 右）。</summary>
    void Receive(HitReaction reaction, int facing);
}
