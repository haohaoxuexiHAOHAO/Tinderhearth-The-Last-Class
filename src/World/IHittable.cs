using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>能挨打的目标。命中结算把反应交给目标自己处理，攻击方不需要知道对面是木桩还是角色。</summary>
/// <remarks>
/// `GP-14` 把训练房的靶从木桩换成真角色时抽出来的：在此之前 <see cref="Hitbox.Resolve"/> 写死只打
/// <see cref="TrainingDummy"/>，于是「用测试角色当靶」这件事在类型上做不到。抽成接口后，木桩（探针用）
/// 与角色（训练房用、将来的敌人）各自实现受击反应，命中路径对两者一视同仁。**只表达「挨打之后怎么
/// 反应」**，命中要不要成立（横向重叠 + 纵深容差）仍在 <see cref="Hitbox"/> 里判，与本接口无关。
/// </remarks>
public interface IHittable
{
    /// <summary>接受一次命中反应；击退方向由攻击方朝向 <paramref name="facing"/> 给出（−1 左 / +1 右）。</summary>
    void Receive(HitReaction reaction, int facing);
}
