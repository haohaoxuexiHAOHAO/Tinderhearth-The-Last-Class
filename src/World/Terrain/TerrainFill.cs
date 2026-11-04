using Godot;

namespace Tinderhearth.World.Terrain;

/// <summary>一种天然地形铺哪张纹理图，或者明确不铺。</summary>
/// <remarks>
/// 挂在 <see cref="DualGridPainter.Fills"/> 里，一种地形一项。纹理图上的像素会按世界坐标换掉显示层里那种地形的主色，
/// 所以它要四边接得上、宽高是格边长的整数倍、只用色板色、整张不透明；规矩在设计仓
/// production/场景绘制约定.md 的「双网格」一节。
///
/// <c>[Tool]</c> 不能去掉，理由与 <see cref="DualGridPair"/> 那一条相同：编辑器里非工具的 C# 资源类取不出来。
/// </remarks>
[Tool]
[GlobalClass]
public partial class TerrainFill : Resource
{
    /// <summary>这一项管的地形，填数据层地形集里的地形编号，从 0 起算。</summary>
    /// <remarks>初值是 -1 而不是 0：0 是一个合法编号，-1 表示「没填」，不填过不了检查。</remarks>
    [Export]
    public int Terrain { get; set; } = -1;

    /// <summary>这种地形铺的纹理图。和 <see cref="NoFill"/> 二选一。</summary>
    [Export]
    public Texture2D? Texture { get; set; }

    /// <summary>勾上表示这种地形明确不铺纹理，照双网格图原样画。和 <see cref="Texture"/> 二选一。</summary>
    [Export]
    public bool NoFill { get; set; }
}
