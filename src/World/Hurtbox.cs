using Godot;

namespace Tinderhearth.World;

/// <summary>独立于实体地形碰撞的受击区域。</summary>
public partial class Hurtbox : Area2D
{
    /// <summary>受击层，不与实体地形层混用。</summary>
    public const uint Layer = 2;

    /// <summary>受击框宽度，世界像素。与实体碰撞框同宽，所以它也是「两个实体不互插的最小间距」。</summary>
    /// <remarks>
    /// 暴露成常量是给探针用的：判定框的伸展是**从美术量出来的**（`ART-6`），于是「木桩放多远才
    /// 既在框内、又不和主角实体互插」这件事必须由这两个量算出来，不能在探针里写死一个距离 ——
    /// 写死的那个数只对当时那版美术成立，美术一改就静默失效（实测 2026-09-09 就是这样断的）。
    /// </remarks>
    public const int WidthWorldPx = 18;
    /// <summary>受击区域所属实体，用于排除自身。</summary>
    public Node2D Actor { get; init; } = null!;

    public override void _Ready()
    {
        CollisionLayer = Layer;
        CollisionMask = 0;
        Monitoring = false;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(WidthWorldPx, 32) },
            Position = new Vector2(0, -16),
        });
    }
}
