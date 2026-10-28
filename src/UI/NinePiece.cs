using Godot;

namespace Tinderhearth.UI;

/// <summary>
/// 一件九宫格界面件：一张图，加四边的边距。四个角不拉伸，夹在中间的那几段按方向拉开。
/// </summary>
/// <remarks>
/// 边距与图放在同一格里，不在素材资源里另开四格：一件九宫格的边距只对那一张图有意义，分开放就会出现
/// 「换了图、忘了改它的边距」，而那不报错，表现只是拉开之后边框被拉花。作者重画了边框的粗细，就在这里
/// 连着改边距；画布尺寸不许变，载入时按清单核（<see cref="UIArtGuard"/>）。
///
/// 字离框多远不在这里：那是内容边距，从排版单位取（<c>rules/UI/UIMetrics.cs</c>）。两样分开，作者改边框粗细
/// 不会顺带挪动框里的字。
/// </remarks>
[GlobalClass]
public partial class NinePiece : Resource
{
    /// <summary>那一张图。</summary>
    [Export]
    public Texture2D? Texture { get; set; }

    /// <summary>左边不拉伸的那几列，像素。</summary>
    [Export]
    public int Left { get; set; }

    /// <summary>上边不拉伸的那几行，像素。</summary>
    [Export]
    public int Top { get; set; }

    /// <summary>右边不拉伸的那几列，像素。</summary>
    [Export]
    public int Right { get; set; }

    /// <summary>下边不拉伸的那几行，像素。</summary>
    [Export]
    public int Bottom { get; set; }
}
