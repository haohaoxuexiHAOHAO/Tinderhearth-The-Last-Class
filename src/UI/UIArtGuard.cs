using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 素材资源的载入核对：没接上、缺格、尺寸不对、九宫格边距撑破画布，一次全报。
/// </summary>
/// <remarks>
/// 判在规则层（<see cref="UIArtCheck"/>），这里只把每一格读到的尺寸与边距交过去。读它的界面在自己的
/// <c>_Ready</c> 里先过这一关再搭，所以缺一张图是启动时一条说清缺哪一格的报错，不是画面上悄悄空一块。
/// </remarks>
public static class UIArtGuard
{
    /// <summary>
    /// 核一份素材资源，全对就把它交回去；没接上或有一处不对就抛，并把全部问题一次列出来。
    /// </summary>
    /// <param name="art">检查器里接的那一份。</param>
    /// <param name="owner">谁要它，例如「Main（节点 Main）」。报错里说给作者听是哪个节点没接上。</param>
    public static UIArt Require(UIArt? art, string owner)
    {
        var resource = art ?? throw new InvalidOperationException(
            $"{owner} 的 {nameof(UIArt)} 没接上 —— 界面件一律从素材资源取，"
                + "把代码仓 assets/self-drawn/ui/ui_art.tres 拖进那一格");

        var problems = UIArtCheck.Problems([.. resource.Cells().Select(ToEntry)]);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"素材资源 {resource.ResourcePath} 有 {problems.Count} 处不对：\n  " + string.Join("\n  ", problems));
        }

        return resource;
    }

    private static UIArtEntry ToEntry((string Group, string Name, Texture2D? Texture, NinePiece? Nine) cell) => new(
        cell.Group,
        cell.Name,
        cell.Texture is { } texture ? new PixelSize(texture.GetWidth(), texture.GetHeight()) : null,
        cell.Nine is { } nine ? new NineMargins(nine.Left, nine.Top, nine.Right, nine.Bottom) : null);
}
