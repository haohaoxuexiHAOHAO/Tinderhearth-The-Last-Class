using System;
using System.Collections.Generic;
using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>
/// 帧级调优用的调试叠层：把判定框与受击框画出来，再加一个暂停与单帧前进。默认关着。
/// </summary>
/// <remarks>
/// 它是世界空间的 <see cref="Node2D"/>，不是界面层。要显示的正是「命中查询用的那个框此刻在世界
/// 哪个位置」，画在世界坐标系里才直接对得上；画在屏幕空间反而要多做一次坐标变换，而那一步正是
/// 可能出错的地方。
///
/// 框都从唯一那一份取，不自己按公式重算：判定框读 <see cref="World.Hitbox.ActiveBoxLocal"/>，
/// 受击框读 <see cref="World.Hurtbox.BoxLocal"/>，两者都是命中与碰撞真正在用的那一份。叠层自己
/// 算一份的话就可能和真实位置慢慢错开 —— 而「画面与判定对不上」恰是这工具要帮人看见的毛病。
///
/// 单帧前进不用 <c>Engine.TimeScale</c>：那会波及输入门面、动画树这些全局的东西。本类只持有
/// 「暂停、请求单步」这两个状态，推进仍由场景循环自己调 —— 场景每帧问一次
/// <see cref="ShouldAdvance"/> 决定这一帧推不推，与顿帧用的是同一个介入点。
/// </remarks>
public partial class CombatDebugOverlay : Node2D
{
    // 全不透明的颜色。本项目的像素素材只用全透明或全不透明，调试线也照这条办，见设计仓
    // production/像素绘制原则.md 的「硬边、抗锯齿与点绘」一节。
    private static readonly Color HitboxColor = new("ff4d4d");
    private static readonly Color HurtboxColor = new("4db8ff");

    /// <summary>叠层总开关。关着时既不画框，也不拦战斗推进。</summary>
    public bool Enabled { get; set; }

    /// <summary>暂停：置真后场景不再推进战斗，除非这一帧请求了单步。只在 <see cref="Enabled"/> 时管用。</summary>
    public bool Paused { get; set; }

    /// <summary>攻击方的判定框节点。画它此刻开着的那个框。</summary>
    public Hitbox Hitbox { get; init; } = null!;

    /// <summary>要画框的那些受击区域，几何各自从自己身上取。</summary>
    public IReadOnlyList<Hurtbox> Hurtboxes { get; init; } = Array.Empty<Hurtbox>();

    private bool _stepRequested;

    /// <summary>请求往前推一帧（暂停时用）。下一次 <see cref="ShouldAdvance"/> 会把它用掉。</summary>
    public void RequestStep() => _stepRequested = true;

    /// <summary>场景每帧问一次：这一帧要不要推进战斗。</summary>
    /// <remarks>
    /// 没开或没暂停时恒为真；暂停时只有请求过单步的那一帧为真，并且问完请求就被清掉。
    /// 所以它有副作用，每帧只能问一次。
    /// </remarks>
    public bool ShouldAdvance()
    {
        if (!Enabled || !Paused)
        {
            return true;
        }

        var step = _stepRequested;
        _stepRequested = false;
        return step;
    }

    /// <summary>这一帧要画的判定框，世界矩形。判定窗没开或没接判定框时是 <c>null</c>。</summary>
    public Rect2? CurrentHitboxWorld => Hitbox?.ActiveBoxLocal is Rect2 local
        ? new Rect2(Hitbox.ToGlobal(local.Position) + VisualDepthOffset(Hitbox.GetParent()), local.Size)
        : null;

    /// <summary>这一帧要画的受击框，逐个给出世界矩形。几何取各受击区域自己那一份。</summary>
    public IEnumerable<Rect2> CurrentHurtboxesWorld()
    {
        foreach (var hurt in Hurtboxes)
        {
            var local = hurt.BoxLocal;
            yield return new Rect2(hurt.ToGlobal(local.Position) + VisualDepthOffset(hurt.Actor), local.Size);
        }
    }

    /// <summary>画框时要补上的那一段纵深绘制偏移，世界像素。</summary>
    /// <remarks>
    /// 碰撞形状本身留在物理位置上不动：命中在物理位置算，纵深靠容差另外补判，不掺绘制偏移。
    /// 但角色精灵是经 <see cref="DepthVisual"/> 按纵深上下挪过的，框只画在物理位置的话，角色一往
    /// 纵深里走框就与画面上的身体错开（实机见过受击框飘在头顶）。所以只在画这一步补同一段偏移。
    /// </remarks>
    private static Vector2 VisualDepthOffset(Node? owner) =>
        owner is IDepthActor actor
            ? new Vector2(0, (float)DepthRendering.DrawOffsetWorldPx(actor.DepthWorldPx))
            : Vector2.Zero;

    public override void _Ready() => Visible = Enabled;

    public override void _Process(double delta)
    {
        Visible = Enabled;
        if (Enabled)
        {
            // 框每帧都在动（角色在走、判定框按段换尺寸），所以每帧重画。关着的时候一点开销都没有。
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!Enabled)
        {
            return;
        }

        foreach (var box in CurrentHurtboxesWorld())
        {
            DrawRect(ToLocalRect(box), HurtboxColor, filled: false, width: 1f);
        }

        if (CurrentHitboxWorld is Rect2 hit)
        {
            DrawRect(ToLocalRect(hit), HitboxColor, filled: false, width: 1f);
        }
    }

    // _Draw 画在本节点自己的坐标系里，所以先把世界矩形转成本地的，这样叠层挂在哪都对得上。
    private Rect2 ToLocalRect(Rect2 world) => new(ToLocal(world.Position), world.Size);
}
