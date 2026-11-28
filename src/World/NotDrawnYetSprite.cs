using Godot;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>场景里一张还没画的图：挂在那个 Sprite2D 上，跑起来画统一占位框，启动日志里列一行。</summary>
/// <remarks>
/// 口径与物品图标、作物素材的「暂无」相同（设计仓 production/像素绘制原则.md 里「作者配的图，没画的那一格写显式的
/// 「暂无」」那一条）：「还没画」要显式写出来，才与漏配分得开；画的是 <see cref="NotDrawnYetArt.Box"/> 那一个框，
/// 大小与正式的图相同，不画剪影。挂着这个脚本，就是那一格写了「暂无」。
///
/// 只给代码不读的那种图用，比如床：代码只认床那个节点，不看它画的是什么。代码要读的图在各自的资源里勾「暂无」
/// （物品图标、作物素材）。图画好了，把脚本卸下、在 Texture 里配上图；脚本还挂着又配了图，启动时当场报错。
/// 框只在跑起来时画上，编辑器里看不见，也不会被存进场景。
/// </remarks>
public sealed partial class NotDrawnYetSprite : Sprite2D
{
    /// <summary>正式的图有多大，像素。占位框画成这么大，两边都至少 1。</summary>
    [Export]
    public Vector2I PictureSize { get; set; }

    public override void _Ready()
    {
        if (Texture is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(NotDrawnYetSprite)}（节点 {Name}）挂着「暂无」，又配了图 —— 图画好了就把这个脚本卸下；"
                    + "还没画就把 Texture 清空");
        }

        if (PictureSize.X < 1 || PictureSize.Y < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(NotDrawnYetSprite)}（节点 {Name}）的 {nameof(PictureSize)} 是 {PictureSize} —— "
                    + "要填正式的图有多大，两边都至少 1 像素");
        }

        Texture = NotDrawnYetArt.Box(PictureSize);
        var where = Owner is null ? Name.ToString() : Owner.GetPathTo(this).ToString();
        GD.Print("[素材] ", where, " 的图还是「暂无」，先画一个 ", PictureSize.X, "×", PictureSize.Y, " 的占位框");
    }
}
