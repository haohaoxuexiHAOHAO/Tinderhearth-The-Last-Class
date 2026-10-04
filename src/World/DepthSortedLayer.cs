using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>
/// 纵深排序层：收集挂在自己下面的纵深角色，每帧按算出来的前后顺序写它们的 <c>z_index</c>。
/// </summary>
/// <remarks>
/// 用容器而不是让各角色自己登记，是为了让「挂进场景」与「参与排序」变成同一件事，少一步会忘的
/// 注册。漏挂的表现是那个角色的前后关系不受纵深影响，而它不报错，所以每次排序都把实际排了几个
/// 记在 <see cref="SortedCount"/> 里，好跟场景里真实的角色数对一下。
///
/// 怎么比前后不在这里，在 <see cref="DepthRendering"/>：那边不碰引擎，能单独测。本层只做引擎
/// 才能做的三件事 —— 找出参与者、把顺序写进 <c>z_index</c>、报出排了几个。
/// </remarks>
public partial class DepthSortedLayer : Node2D
{
    /// <summary>角色层的 <c>z_index</c> 起点。地形与背景留在 0 及以下，角色从这里往上叠。</summary>
    /// <remarks>
    /// 取 8 而不是紧贴 0，是给将来的前景装饰留出层号 —— 那些东西要按纵深插进角色之间，就得在
    /// 角色之下、地面之上有号可用。真到那一步再定怎么分，现在不预设。
    /// </remarks>
    public const int ActorZBase = 8;

    private readonly List<Node2D> _nodes = [];
    private readonly List<DepthSubject> _subjects = [];

    /// <summary>上一次排序实际排了几个角色。拿来跟场景里真实的角色数对一下，好发现漏挂。</summary>
    public int SortedCount { get; private set; }

    /// <summary>重排一次。</summary>
    /// <remarks>
    /// 由场景在角色推进之后显式调。顿帧冻结的那几帧不调，于是绘制顺序跟着一起冻住，
    /// 前后关系不会在冻结帧里跳动。
    /// </remarks>
    public void Sort()
    {
        _nodes.Clear();
        _subjects.Clear();
        foreach (var child in GetChildren())
        {
            // 两个条件都要：写 z_index 得是 Node2D，取排序用的量得是 IDepthActor。
            if (child is not Node2D node || child is not IDepthActor actor)
            {
                continue;
            }
            _nodes.Add(node);
            // 排序用的量由角色自己给（它转发可视根算出的那一份），本层不重算一遍。
            _subjects.Add(actor.DepthSubject);
        }

        var order = DepthRendering.DrawOrder(_subjects);
        for (var rank = 0; rank < order.Length; rank++)
        {
            _nodes[order[rank]].ZIndex = ActorZBase + rank;
        }
        SortedCount = order.Length;
    }
}
