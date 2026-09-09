using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>Active 硬门后的实时形状查询，不依赖延迟的 Area 重叠缓存。</summary>
public partial class Hitbox : Area2D
{
    private readonly HashSet<ulong> _hit = new();
    private readonly RectangleShape2D _shape = new() { Size = SizeFor(ComboKind.Light) };
    private bool _wasActive;

    /// <summary>轻重各自的判定框尺寸，世界像素。取值依据见 <see cref="CombatFeel"/>。</summary>
    public static Vector2 SizeFor(ComboKind kind) => kind == ComboKind.Heavy
        ? new Vector2(CombatFeel.HeavyHitboxWidthWorldPx, CombatFeel.HeavyHitboxHeightWorldPx)
        : new Vector2(CombatFeel.LightHitboxWidthWorldPx, CombatFeel.LightHitboxHeightWorldPx);
    /// <summary>当前逻辑帧是否开放检测。</summary>
    public bool IsActive { get; private set; }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = false;
        AddChild(new CollisionShape2D { Shape = _shape });
    }

    /// <summary>移动与落地取消完成后调用；每段 Active 起点重置去重集合。</summary>
    public int Resolve(PlayerActor player, Action<HitReaction> feedback)
    {
        IsActive = player.Combat.Combo.IsHitActive;
        if (IsActive && !_wasActive) _hit.Clear();
        _wasActive = IsActive;
        if (!IsActive) return 0;
        // 框按轻重换尺寸：贴住画面上那一拳真正伸到的距离，重击更远就真的更远（`ART-6` 实测）。
        _shape.Size = SizeFor(player.Combat.Combo.Kind);
        Position = new Vector2(player.Combat.Motor.Facing * _shape.Size.X / 2f,
                               -CombatFeel.HitboxCenterYWorldPx);
        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = _shape, Transform = GlobalTransform, CollisionMask = Hurtbox.Layer,
            CollideWithAreas = true, CollideWithBodies = false,
        };
        var count = 0;
        foreach (var item in GetWorld2D().DirectSpaceState.IntersectShape(query))
        {
            if (item["collider"].AsGodotObject() is not Hurtbox hurt || hurt.Actor == player
                || hurt.Actor is not TrainingDummy dummy || !_hit.Add(hurt.GetInstanceId())) continue;
            var reaction = HitResolution.Resolve(player.Combat.Combo.Kind);
            dummy.Receive(reaction, player.Combat.Motor.Facing);
            feedback(reaction);
            count++;
        }
        return count;
    }
}
