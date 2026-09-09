using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>
/// 纵深排序层（`ENG-15`）：收集挂在自己下面的纵深角色，每帧按规则层的顺序写 <c>z_index</c>。
/// </summary>
/// <remarks>
/// **为什么用容器而不是让各角色自己登记**：挂进场景与参与排序合成同一件事，少一步能忘的注册。
/// 漏挂的表现是那个角色的前后关系不受纵深影响 —— 不报错，所以本层每次排序都**自报覆盖量**
/// （<see cref="SortedCount"/>），探针核它等于场景里真实的角色数。
///
/// **排序逻辑不在这里**，在 <see cref="DepthRendering"/>：两个键怎么比、相等时怎么定，那些能脱
/// 引擎单测。本层只做三件引擎的事：找出参与者、把顺序写进 <c>z_index</c>、报覆盖量。
///
/// **不派生 <c>Camera2D</c>、不动 <c>Main.tscn</c>**：本层是普通 <c>Node2D</c>，`check_camera.py`
/// 那条「派生 Camera2D 的类型恰好一个」不受影响。
/// </remarks>
public partial class DepthSortedLayer : Node2D
{
    /// <summary>
    /// 角色层的 <c>z_index</c> 起点。地形与背景留在 0 及以下，角色从这里往上叠。
    /// </summary>
    /// <remarks>
    /// 留 8 的余量而不是紧贴 0：将来关卡的前景装饰（正典说的纵深上的阻挡与掩体）要按纵深插进
    /// 角色之间，那时需要在角色之下、地面之上有可用的层号。真到那一步再定分层，本轮不预设。
    /// </remarks>
    public const int ActorZBase = 8;

    private readonly List<Node2D> _nodes = [];
    private readonly List<DepthSubject> _subjects = [];

    /// <summary>上一次排序实际排了几个角色。**自报覆盖量**，给探针核。</summary>
    public int SortedCount { get; private set; }

    /// <summary>
    /// 重排一次。**由场景在角色推进之后显式调用** —— 顿帧冻结时不调，绘制顺序跟着一起冻住。
    /// </summary>
    public void Sort()
    {
        _nodes.Clear();
        _subjects.Clear();
        foreach (var child in GetChildren())
        {
            // 两个条件都要：写 z_index 要 Node2D，排序键要 IDepthActor。
            if (child is not Node2D node || child is not IDepthActor actor)
            {
                continue;
            }
            _nodes.Add(node);
            // 键由角色自己给（它转发可视根算出的那一份），本层不重算 —— 重算就是第二份事实。
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
