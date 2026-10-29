using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 把系统鼠标指针换成作者画的那三张，照画面当前的整数倍最近邻放大；倍数一变就重换一遍。
/// </summary>
/// <remarks>
/// 倍数怎么取、热点放大后在哪在 <see cref="PixelCursor"/>。每帧比一次倍数：拉窗口、切全屏都会改它，不必各订阅一遍；
/// 没变就什么都不做。暂停时照样跑，开着手环拉窗口也跟着换。
///
/// 显示后端不支持自定义指针时（无头跑）打一行就停：那时没有指针可换，不算配错。
///
/// 手形与禁止那两张只在按钮这一类能点的控件上出现，哪一种用哪一张在 <see cref="ApplyButtonShape"/>。
/// </remarks>
public sealed partial class CursorSkin : Node
{
    private (Texture2D Image, UIArtPiece Piece, Input.CursorShape Shape)[] _cursors = [];
    private int _scale;

    /// <summary>素材资源。三张指针从它取，缺了或缺格当场报错并一次列全。</summary>
    /// <remarks>接的是 <c>assets/self-drawn/ui/ui_art.tres</c>。</remarks>
    [Export]
    public UIArt? Art { get; set; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        // 先停掉每帧回调，校验过了再开：_Ready 抛出之后引擎并不会停掉这个节点。
        SetProcess(false);
        var art = UIArtGuard.Require(Art, $"{nameof(CursorSkin)}（节点 {Name}）");

        if (!DisplayServer.HasFeature(DisplayServer.Feature.CustomCursorShape))
        {
            GD.Print("[界面] 鼠标指针 显示后端 ", DisplayServer.GetName(), " 不支持自定义指针，照系统的");
            return;
        }

        // 哪一张对哪一种系统指针：控件的鼠标指针形状选的是这三种之一，系统就换成对应那一张。
        _cursors =
        [
            (Piece(art.KitCursorArrow, PixelCursor.Arrow), PixelCursor.Arrow, Input.CursorShape.Arrow),
            (Piece(art.KitCursorHand, PixelCursor.Hand), PixelCursor.Hand, Input.CursorShape.PointingHand),
            (Piece(art.KitCursorBlocked, PixelCursor.Blocked), PixelCursor.Blocked, Input.CursorShape.Forbidden),
        ];
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        var scale = PixelCursor.ScaleOf(GetViewport().GetFinalTransform().Scale.X);
        if (scale == _scale)
        {
            return;
        }

        _scale = scale;
        foreach (var (texture, piece, shape) in _cursors)
        {
            // 每次从原图放大，不在上一次放大过的那张上再放：倍数变小时那样放不回去。
            var image = texture.GetImage();
            image.Resize(image.GetWidth() * scale, image.GetHeight() * scale, Image.Interpolation.Nearest);
            var hotspot = PixelCursor.HotspotOf(piece, scale);
            Input.SetCustomMouseCursor(image, shape, new Vector2(hotspot.X, hotspot.Y));
        }

        GD.Print("[界面] 鼠标指针 按 ", scale, " 倍放大");
    }

    /// <summary>
    /// 按钮这一类能点的控件指着时用哪一种指针：能点是手形，灰掉点不了是禁止那张。别处照旧是箭头。
    /// </summary>
    /// <remarks>
    /// 引擎没有「禁用变了」的信号，所以改 <c>Disabled</c> 的那一处跟着调一次。
    /// </remarks>
    public static void ApplyButtonShape(BaseButton button) =>
        button.MouseDefaultCursorShape = button.Disabled
            ? Control.CursorShape.Forbidden
            : Control.CursorShape.PointingHand;

    private static Texture2D Piece(Texture2D? texture, UIArtPiece piece) =>
        HudPlace.Piece(texture, $"{piece.Group}/{piece.Name}");
}
