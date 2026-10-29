using Godot;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 世界那一层能往它身上挂东西的目标：报画出来的脚底是哪个节点、头顶离脚底多高、哪一档体型。
/// </summary>
/// <remarks>
/// 往身上挂东西的那几个方法只收它，所以没报的目标编译都过不了；报了 0 或没选档的，挂的那一刻当场抛
/// （<see cref="WorldUIAnchor"/>）。侧视角色现在是代码搭的，由角色自己声明；换成作者在场景里放的锚点节点归设计仓台账 GP-14。
/// </remarks>
public interface IWorldUITarget
{
    /// <summary>画出来的脚底：世界那一层的东西跟着这个节点走。</summary>
    /// <remarks>
    /// 侧视角色报它的纵深可视根、不报角色本体：精灵按纵深上下挪过，本体留在物理位置上，跟本体走的话，
    /// 站到纵深带后面时血条会落进身体里。
    /// </remarks>
    Node2D WorldUIFeet { get; }

    /// <summary>头顶离脚底多高，世界像素。</summary>
    int HeadHeightWorldPx { get; }

    /// <summary>体型档。</summary>
    BodyTier BodyTier { get; }
}
