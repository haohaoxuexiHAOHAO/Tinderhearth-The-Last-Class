namespace Tinderhearth.Rules.UI;

/// <summary>
/// 素材资源里的一格：生成器清单里的一件界面件该是什么样。
/// </summary>
/// <param name="Group">在 <c>assets/self-drawn/ui/</c> 下哪个目录，也是检查器里的分组。</param>
/// <param name="Name">文件名去掉 <c>.png</c>，也是那一格的名字。</param>
/// <param name="Size">画布该多大。作者换图要保持原尺寸，载入时按它核。</param>
/// <param name="Nine">九宫格四边的边距；不拉伸的件是 <c>null</c>。</param>
/// <param name="State">同一件的哪一态（<c>normal</c>、<c>focus</c>……）；只有一态的件是空串。</param>
/// <param name="Use">它用在哪，照生成器清单那一行。</param>
/// <param name="Hotspot">鼠标指针点下去的那一像素，按原图算；不是指针的件是 <c>null</c>。</param>
/// <param name="Glyphs">字形表从左到右是哪几个字，一个字一格、等宽；不是字形表的件是 <c>null</c>。</param>
/// <remarks>
/// 全部格子在 <see cref="UIArtManifest"/>，那一份由代码仓 <c>tools/ui_art_resource.py</c> 照生成器清单写出，
/// 热点与字形顺序也是：它们的家是生成器，代码里不另写一份。
/// </remarks>
public sealed record UIArtPiece(
    string Group, string Name, PixelSize Size, NineMargins? Nine, string State, string Use,
    PixelPoint? Hotspot = null, string? Glyphs = null)
{
    /// <summary>相对 <c>assets/self-drawn/</c> 的路径，例如 <c>ui/kit/frame_thick_dark.png</c>。</summary>
    public string FilePath => $"ui/{Group}/{Name}.png";

    /// <summary>报错里用的那一句：路径、尺寸、哪一态、用在哪。</summary>
    public string Describe() =>
        $"{FilePath}（{Size}{(State.Length > 0 ? $"，{State} 那一态" : "")}；{Use}）";
}

/// <summary>一张图的像素尺寸。</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    /// <summary>写成「宽×高」，与生成器清单、界面绘制约定里的写法一样。</summary>
    public override string ToString() => $"{Width}×{Height}";
}

/// <summary>图上的一个像素位置，从左上角数起。</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    /// <summary>写成「(X,Y)」，与生成器清单里的写法一样。</summary>
    public override string ToString() => $"({X},{Y})";
}

/// <summary>九宫格四边的边距，像素。四个角不拉伸，夹在中间的那几段按方向拉开。</summary>
public readonly record struct NineMargins(int Left, int Top, int Right, int Bottom)
{
    /// <summary>写成「左 上 右 下」，与生成器清单里的写法一样。</summary>
    public override string ToString() => $"左 {Left} 上 {Top} 右 {Right} 下 {Bottom}";
}

/// <summary>
/// 素材资源里实际填了什么：一格的图多大、九宫格边距填了多少。
/// </summary>
/// <param name="Group">那一格在哪个分组。</param>
/// <param name="Name">那一格的名字。</param>
/// <param name="Size">填进去的那张图多大；没填图是 <c>null</c>。</param>
/// <param name="Nine">九宫格那一格填的边距；不是九宫格件、或那一格整个空着，是 <c>null</c>。</param>
/// <remarks>
/// 它只有尺寸、没有图本身：规则层不认识 Godot 的贴图，引擎层把读到的尺寸交过来，核对在这一侧做、测试也在这一侧。
/// </remarks>
public readonly record struct UIArtEntry(string Group, string Name, PixelSize? Size, NineMargins? Nine);
