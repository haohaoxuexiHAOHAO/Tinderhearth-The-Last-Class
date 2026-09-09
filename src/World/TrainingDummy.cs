using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>不还手的受击木桩；唯一拥有其状态推进。</summary>
public partial class TrainingDummy : CharacterBody2D
{
    /// <summary>统一状态载体。</summary>
    public StatusEffects Statuses { get; } = new();
    /// <summary>剩余闪白逻辑帧。</summary>
    public int FlashRemaining { get; private set; }
    /// <summary>已接受命中次数，开发探针用。</summary>
    public int HitCount { get; private set; }
    private float _step;

    public override void _Ready()
    {
        CollisionLayer = 4;
        CollisionMask = 1;
        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(18, 32) }, Position = new Vector2(0, -16) });
        AddChild(new Hurtbox { Actor = this });
    }

    /// <summary>新命中替换剩余硬直与位移，不叠加。</summary>
    public void Receive(HitReaction reaction, int facing)
    {
        Statuses.Apply(StatusKind.Hitstun, reaction.HitstunFrames);
        _step = facing * (float)reaction.KnockbackWorldPx / reaction.HitstunFrames;
        FlashRemaining = CombatFeel.FlashFrames;
        HitCount++;
        QueueRedraw();
    }

    /// <summary>旧硬直每帧消费一次位移；第 N 次消费后到期，无尾滑。</summary>
    public void AdvanceCombat()
    {
        var moving = Statuses.Has(StatusKind.Hitstun);
        Statuses.Tick();
        if (moving) MoveAndCollide(new Vector2(_step, 0));
        if (!Statuses.Has(StatusKind.Hitstun)) _step = 0;
        if (FlashRemaining > 0) FlashRemaining--;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(-9, -32, 18, 32), FlashRemaining > 0 ? Colors.White : new Color("b85450"));
        DrawRect(new Rect2(-13, -22, 26, 5), FlashRemaining > 0 ? Colors.White : new Color("d99863"));
    }
}
