namespace Tinderhearth.Rules.UI;

/// <summary>
/// 鼠标指针：作者画的三张各是素材清单里哪一件、按几倍放大、热点放大后落在哪一点。
/// </summary>
/// <remarks>
/// 系统指针按屏幕像素画、不跟着画面放大，所以照画面当前的整数倍最近邻放大原图再交给系统。这一条出自 Godot 论坛
/// 对 4.6 的说法，在 4.7.2 上实测归设计仓台账 UI-46。热点在素材清单里，家在生成器，这里不另写一份。
/// </remarks>
public static class PixelCursor
{
    /// <summary>引擎收的指针图最长边，像素。再大它不收（Godot 文档 Input.set_custom_mouse_cursor）。</summary>
    public const int MaxSide = 256;

    /// <summary>平常那一张。</summary>
    public static UIArtPiece Arrow { get; } = UIArtManifest.Find("kit", "cursor_arrow");

    /// <summary>指着能点的东西时那一张。</summary>
    public static UIArtPiece Hand { get; } = UIArtManifest.Find("kit", "cursor_hand");

    /// <summary>指着点不了的东西时那一张。</summary>
    public static UIArtPiece Blocked { get; } = UIArtManifest.Find("kit", "cursor_blocked");

    /// <summary>三张都在这里，引擎层逐张换上。</summary>
    public static IReadOnlyList<UIArtPiece> All { get; } = [Arrow, Hand, Blocked];

    /// <summary>
    /// 画面现在放大了 <paramref name="viewScale"/> 倍时，指针放大几倍：取下整，至少 1 倍，放大之后不超过 <see cref="MaxSide"/>。
    /// </summary>
    /// <remarks>
    /// 取下整而不四舍五入：画面不是整数倍时，指针照旧按整数倍放大才不糊，宁可小一点。
    /// </remarks>
    public static int ScaleOf(double viewScale) =>
        Math.Clamp((int)Math.Floor(viewScale), 1, MaxSide / All.Max(p => Math.Max(p.Size.Width, p.Size.Height)));

    /// <summary>某一张放大 <paramref name="scale"/> 倍之后热点在哪：原图那一像素放大后那一块的左上角。</summary>
    public static PixelPoint HotspotOf(UIArtPiece cursor, int scale)
    {
        var hotspot = cursor.Hotspot ?? throw new ArgumentException(
            $"素材清单里 {cursor.FilePath} 没有热点：它不是指针，或者生成器（tools/ui_art.py）那一件没带 hotspot",
            nameof(cursor));
        return new PixelPoint(hotspot.X * scale, hotspot.Y * scale);
    }
}
