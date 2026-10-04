using Godot;

namespace Tinderhearth.World;

/// <summary>独立于实体地形碰撞的受击区域。</summary>
public partial class Hurtbox : Area2D
{
    /// <summary>受击层，不与实体地形层混用。</summary>
    public const uint Layer = 2;

    /// <summary>受击框宽度，世界像素。与实体碰撞框同宽，所以它也是「两个实体不互插的最小间距」。</summary>
    /// <remarks>
    /// 公开成常量，是为了让「目标摆多远才既在判定框内、又不和主角的身体互插」这个距离由它算出来。
    /// 判定框伸多远是从美术量出来的，换一版美术那个距离就变 —— 在别处写死一个数只对某一版美术成立，
    /// 美术一改就静默失效（实测就是这样断过）。
    /// </remarks>
    public const int WidthWorldPx = 18;

    /// <summary>受击框高度，世界像素，从脚底往上量。按角色给，取该角色自己实测的本体高度。</summary>
    /// <remarks>
    /// 没有默认值、必须显式填。曾经写死 32，而 32 是木桩柱子的高度，主角实测本体只有 28；
    /// 多出来的那 4 像素让从头顶掠过的攻击照样判命中，玩家读到「明明躲过了却被打到」，而它不报错。
    /// 写成必填的 <c>init</c> 属性，接新角色时编译器就逼着调用方先量一次它多高。
    ///
    /// 逐帧跟着精灵变高刻意不做：整帧的不透明高度会在 26 到 30 之间脉动，而受击框脉动会让同一招
    /// 有时中有时不中、读不出原因。按状态分（站立、滞空、闪避、将来的蹲伏）才是动作游戏的常规做法，
    /// 那要先定设计，所以现在只把站立这一个值摆正。
    /// </remarks>
    public required int HeightWorldPx { get; init; }

    /// <summary>受击区域所属实体，用于排除自身。</summary>
    public Node2D Actor { get; init; } = null!;

    /// <summary>受击框的本地矩形（脚底在原点、框在脚底之上）。碰撞形状与调试叠层共用它，不各写一份。</summary>
    public Rect2 BoxLocal => new(new Vector2(-WidthWorldPx / 2f, -HeightWorldPx),
                                 new Vector2(WidthWorldPx, HeightWorldPx));

    public override void _Ready()
    {
        CollisionLayer = Layer;
        CollisionMask = 0;
        Monitoring = false;
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = BoxLocal.Size },
            Position = BoxLocal.Position + BoxLocal.Size / 2f,
        });
    }
}
