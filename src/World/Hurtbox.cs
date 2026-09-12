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
    /// <summary>受击框高度，世界像素（脚底原点向上）。**按角色给**，取该角色自己的实测本体高度。</summary>
    /// <remarks>
    /// **不设默认值、必须显式给**（2026-09-12 改）：原先这里写死 32，而 32 是**木桩柱子**的高度，
    /// 主角实测本体只有 28（登记表「角色本体」，`PlayerActor` 的碰撞框早就绑了这个 28 并写明理由）。
    /// 于是同一个仓里对「主角多高」有两份真相，受击框那份高出 4px —— 表现是从头顶 1–4px 掠过的攻击
    /// 照样判命中，玩家侧读到「我明明躲过了却被打到」，而这**不报错**。是 `ENG-6` 的调试叠层把它显
    /// 出来的。写成必填的 `init` 属性，新角色接进来时编译器就逼着调用方回答「它多高」，答不上来才
    /// 是该停下来量一次的时候。
    ///
    /// **逐帧自适应刻意不做。** `ENG-15` 的影子踩过同一形状：整帧不透明边界含四肢，`walk` 的逐帧
    /// 宽度在 11–20px 之间摆，当时被迫加下限。高度同理会在 26–30 之间脉动，而受击框脉动会让命中
    /// 学不会 —— 同一招有时中有时不中且读不出原因。按**状态**分（站立／滞空／闪避／将来的蹲伏）才
    /// 是动作游戏的常规做法，那要设计，归 `GP-18` 与 `GP-6`。本轮只把「站立值」摆正。
    /// </remarks>
    public required int HeightWorldPx { get; init; }

    /// <summary>受击区域所属实体，用于排除自身。</summary>
    public Node2D Actor { get; init; } = null!;

    /// <summary>受击框的本地矩形（脚底在原点、框在脚底之上）。碰撞形状与调试叠层（`ENG-6`）共用它，不各写一份。</summary>
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
