using System;
using System.Collections.Generic;
using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>
/// `ENG-6` 帧级调优叠层：判定框／受击框可视化 ＋ 帧步进（暂停／单帧前进）。**默认关，仅调试开。**
/// </summary>
/// <remarks>
/// **世界空间 `Node2D`，不走 `CanvasLayer`。** 它要显示的正是「命中查询实际用的那个框在世界哪个
/// 位置」，所以画在世界坐标系里、直接对上角色与判定框的真实位置最不会错；屏幕空间画反而要多一次
/// world→screen 变换，而那一维恰是这工具存在的意义（`SPEC` §2.2 把它记成 `CanvasLayer` 是早期的
/// 节点类型猜测，dev 场景是纯 `Node2D` 世界、不建 `UiRoot`，见 `DepthDev`）。不派生 `Camera2D`、
/// 不引用 `UiLayer`，因此不被 `check_camera`／`check_worldui` 管到。
///
/// **框从单一真相取，不各算一份。** 判定框读 <see cref="World.Hitbox.ActiveBoxLocal"/>（命中查询用
/// 的同一份 `_shape`），受击框读 <see cref="World.Hurtbox.BoxLocal"/>（碰撞形状用的同一份）。叠层
/// 若自己按公式重算，就可能和真实命中位置漂移 —— 而「画面与判定对不上」正是这工具要帮人看见的病。
///
/// **帧步进复用顿帧同一个介入点。** 叠层只持有「暂停／请求单步」状态，**推进仍由场景循环调
/// <c>AdvanceCombat</c>**：场景每帧问 <see cref="ShouldAdvance"/> 决定这一帧推不推，与 `Hitstop` 的
/// 「当帧不推进」同构。**不用 <c>Engine.TimeScale</c>** —— 那会波及 `InputRouter`、动画树等全局
/// （`SPEC` §10.2 的风险），而手动 gate 只冻结战斗推进、可测、无引擎全局副作用。
/// </remarks>
public partial class CombatDebugOverlay : Node2D
{
    // 不透明色（`像素绘制原则` §9：透明度只用全透明或全不透明，调试线也不例外）。
    private static readonly Color HitboxColor = new("ff4d4d");
    private static readonly Color HurtboxColor = new("4db8ff");

    /// <summary>叠层总开关。默认关：不画、不 gate 推进（<see cref="ShouldAdvance"/> 恒真）。</summary>
    public bool Enabled { get; set; }

    /// <summary>帧步进：暂停时场景不推进战斗，除非本帧请求了单步。仅在 <see cref="Enabled"/> 时生效。</summary>
    public bool Paused { get; set; }

    /// <summary>攻击方的判定框节点；画它当前 Active 帧的框。</summary>
    public Hitbox Hitbox { get; init; } = null!;

    /// <summary>要画受击框的受击区域，各自持有自己的 <see cref="World.Hurtbox.BoxLocal"/>。</summary>
    public IReadOnlyList<Hurtbox> Hurtboxes { get; init; } = Array.Empty<Hurtbox>();

    private bool _stepRequested;

    /// <summary>请求推进一帧（暂停时用）。下一次 <see cref="ShouldAdvance"/> 会消费它。</summary>
    public void RequestStep() => _stepRequested = true;

    /// <summary>
    /// 场景每帧问一次：这一帧要不要推进战斗。未开启或未暂停时恒真；暂停时只有请求了单步的那一帧
    /// 为真，并消费掉该请求。**有副作用（清单步标志），每帧只问一次。**
    /// </summary>
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

    /// <summary>当前要画的判定框（世界矩形）；非 Active 帧或没接判定框时为 <c>null</c>。</summary>
    public Rect2? CurrentHitboxWorld => Hitbox?.ActiveBoxLocal is Rect2 local
        ? new Rect2(Hitbox.ToGlobal(local.Position) + VisualDepthOffset(Hitbox.GetParent()), local.Size)
        : null;

    /// <summary>当前要画的受击框（世界矩形）逐个。几何取自各受击区域自己的 <see cref="World.Hurtbox.BoxLocal"/>。</summary>
    public IEnumerable<Rect2> CurrentHurtboxesWorld()
    {
        foreach (var hurt in Hurtboxes)
        {
            var local = hurt.BoxLocal;
            yield return new Rect2(hurt.ToGlobal(local.Position) + VisualDepthOffset(hurt.Actor), local.Size);
        }
    }

    /// <summary>
    /// 框要跟着角色精灵的纵深绘制偏移一起挪（`ENG-15`／`GP-14` 阶段 1 实机）。
    /// </summary>
    /// <remarks>
    /// 判定框与受击框的**碰撞形状留在物理位置**——命中判定在物理位置算、纵深靠容差补判（`GP-16`），
    /// 不掺绘制偏移。但角色精灵经 <see cref="DepthVisual"/> 按纵深上下偏移，框若只画在物理位置，角色
    /// 一往纵深里走框就与画面上的身体错开（实机发现：纵深不在带中线时受击框飘在头顶）。所以**只有
    /// 叠层显示时**把框补上同一段偏移，与看得见的精灵对齐；物理位置那份一点不动。带中线（默认纵深）
    /// 偏移为零，所以在带中线上验收的 `ENG-6` 探针不受影响。
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
            // 框每帧动（角色在走、判定框按段变），所以每帧重画。默认关时不重画、不产生开销。
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

    // _Draw 在本节点本地坐标系里画；把世界矩形转本地再画，这样叠层挂在哪都对得上。
    private Rect2 ToLocalRect(Rect2 world) => new(ToLocal(world.Position), world.Size);
}
