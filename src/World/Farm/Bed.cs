using Godot;
using Tinderhearth.Rules.Economy;

namespace Tinderhearth.World.Farm;

/// <summary>床：作者摆在农场里的一个交互点。角色站进它的范围里按交互键就睡，推进一天。</summary>
/// <remarks>
/// 床自己不听按键。交互键只在 <see cref="FarmField"/> 那一处听：按下那一刻问每一张床角色在不在它的范围里，
/// 再由规则层挑是睡还是作用在当前操作格上（<see cref="FarmInteraction"/>）。两处各听各的话，同一下按键先被谁
/// 接住就看引擎的调用先后，而那不报错。
///
/// 床在 _Ready 里把自己登记到检查器里接的那一片地上。没摆床的农场只能按推进一天那个调试键过一天，那不算配错：床迟早要
/// 搬进室内（设计仓台账 GP-130）。摆了床却缺了哪一格，当场报错说清缺谁。
/// </remarks>
public partial class Bed : Node2D
{
    /// <summary>可交互范围：一个 Area2D，下面挂作者画的碰撞形状。</summary>
    /// <remarks>
    /// 掩码要勾上角色所在的层、Monitoring 要开着，否则它永远看不见角色走进来，而那不报错，所以启动时就验。
    /// 范围画多大由作者实机定：站在看起来该能睡的地方能睡，走开几步再按是锄地或浇水。
    /// </remarks>
    [Export]
    public Area2D? Reach { get; set; }

    /// <summary>谁站进范围里算站在床边：农场里那个角色。</summary>
    [Export]
    public CharacterBody2D? Sleeper { get; set; }

    /// <summary>睡下去推进的是哪一片地的那一天。</summary>
    [Export]
    public FarmField? Field { get; set; }

    // 下面两个是 _Ready 里校验过的非空副本。
    private Area2D _reach = null!;
    private CharacterBody2D _sleeper = null!;
    private FarmField? _registeredTo;

    /// <summary>角色这一刻在不在床的范围里。</summary>
    /// <remarks>
    /// 读的是引擎上一次物理同步攒下的重叠，按键这种慢事够用
    /// （设计仓 reference/godot/交互点与站位点.md 的「自己查（按键或点一下那一类）」一节）。
    /// </remarks>
    public bool SleeperInReach => _reach.OverlapsBody(_sleeper);

    public override void _Ready()
    {
        _reach = Reach ?? throw Missing(nameof(Reach), "可交互范围（一个 Area2D，下面挂碰撞形状）");
        _sleeper = Sleeper ?? throw Missing(nameof(Sleeper), "农场里那个角色：站进范围里的是他，才算站在床边");
        var field = Field ?? throw Missing(nameof(Field), $"睡下去推进的那一片地（挂了 {nameof(FarmField)} 的节点）");

        if (!_reach.GetChildren().Any(HasArea))
        {
            throw new InvalidOperationException(
                $"{nameof(Bed)}（节点 {Name}）的范围（{_reach.Name}）下面没有能用的碰撞形状 —— 要一个画了形状的 "
                    + "CollisionShape2D 或 CollisionPolygon2D，而且没被禁用；没有它，这个范围谁都碰不到");
        }

        if (!_reach.Monitoring)
        {
            throw new InvalidOperationException(
                $"{nameof(Bed)}（节点 {Name}）的范围（{_reach.Name}）关着 Monitoring —— 关着它就不记谁走了进来，"
                    + "角色站进去也睡不了");
        }

        if ((_reach.CollisionMask & _sleeper.CollisionLayer) == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(Bed)}（节点 {Name}）的范围（{_reach.Name}）掩码勾的层与角色（{_sleeper.Name}）所在的层一层都对不上"
                    + $"（掩码：{Layers(_reach.CollisionMask)}；角色：{Layers(_sleeper.CollisionLayer)}）—— "
                    + "掩码要勾上角色所在的层，否则角色站进去它也看不见");
        }

        field.RegisterBed(this);
        _registeredTo = field;
    }

    public override void _ExitTree()
    {
        _registeredTo?.UnregisterBed(this);
        _registeredTo = null;
    }

    private static bool HasArea(Node child) => child switch
    {
        CollisionShape2D shape => shape.Shape is not null && !shape.Disabled,
        CollisionPolygon2D polygon => polygon.Polygon.Length >= 3 && !polygon.Disabled,
        _ => false,
    };

    /// <summary>一个层掩码写成检查器里那样的层号，从 1 数起。</summary>
    private static string Layers(uint mask)
    {
        var numbers = Enumerable.Range(0, 32).Where(i => (mask & (1u << i)) != 0).Select(i => i + 1).ToList();
        return numbers.Count == 0 ? "一层都没有" : $"第 {string.Join("、", numbers)} 层";
    }

    private InvalidOperationException Missing(string exportName, string what) =>
        new($"{nameof(Bed)}（节点 {Name}）的 {exportName} 没接上 —— 它要的是{what}");
}
