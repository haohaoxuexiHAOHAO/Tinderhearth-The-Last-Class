using Godot;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>
/// 当前操作格（`UI-27`）：框出玩家这一下会作用在哪一格。
/// </summary>
/// <remarks>
/// 规则的权威是设计仓 `design/界面系统.md` 的「世界里那一层：当前操作格，两个设备族共用一个读口」，
/// 本类不复述那几条理由。落成代码的三条形状：
///
/// <list type="bullet">
/// <item>**算法按设备族分两条，读口只有一个**（<see cref="Cell"/>）。这一条是承重的：锄、播、浇、
/// 收以及将来的建造摆位都读同一个口，分成两条调用路径之后每加一个逐格动作就要在两侧各接一次，
/// 而漏接哪一侧不报错 —— 表现只是「手柄玩家用不了这个动作」。</item>
/// <item>**够不到的格照样画框，只是画成不可用**（<see cref="IsWithinReach"/> 为 false）。指针能指到
/// 屏幕任何地方，完全不画框的话玩家按下去没反应而找不到原因。</item>
/// <item>**只勾一格的边，不做整格高亮。** 高亮会盖住那一格上的作物，而作物正是玩家要判断的东西 ——
/// 他要看的往往正是「这一格能不能收」。</item>
/// </list>
///
/// **可及范围的形状是十字、不含斜角**，理由不是距离而是朝向只有四个：指向斜角时角色没有对应的
/// 朝向可转，表现就是他身子朝正面而手在斜着锄。
/// </remarks>
public partial class FarmCellCursor : Node2D
{
    /// <summary>
    /// 拿格坐标用的那一层。**世界坐标与格坐标的换算只经它一处。**
    /// </summary>
    /// <remarks>
    /// 接哪一层都行（两层图层共用同一套网格），但**必须与 <see cref="FarmField"/> 接的是同一套网格** ——
    /// 接了不同格大小的两层之后框与实际作用的格会错开，而那不报错。
    /// </remarks>
    [Export]
    public TileMapLayer? GridLayer { get; set; }

    /// <summary>角色。手柄那一侧要读它的朝向，两侧都要读它站在哪一格。</summary>
    [Export]
    public FarmWalker? Player { get; set; }

    /// <summary>输入门面。本类只读它的「最后用的是哪族设备」。</summary>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>
    /// 可及范围，按格数量。**形状是十字、不含斜角**，本项只定它伸多远。
    /// </summary>
    /// <remarks>
    /// **刻意没有默认值**：大小归 `GP-6`，要实机试才定得出来（登记在设计仓 `design/数值模型.md`
    /// 的尚未给值表）—— 太紧则鼠标指偏一点就够不到，太松则角色看起来手伸得过长。
    ///
    /// 至少 1：设计定的形状是「脚下那一格**加**上下左右四格」，填 0 就只剩脚下那一格，
    /// 那是另一种形状、不是这一种的一个取值。
    /// </remarks>
    [Export]
    public int ReachInCells { get; set; }

    /// <summary>够得到时框的颜色。</summary>
    /// <remarks>
    /// **颜色归作者在检查器里定**（`ADR-0009`），所以它不进 `rules/UI/HudPalette.cs` ——
    /// 那一份是 HUD 的占位色板、等 `DOC-2` 定稿，而这一格是世界里那一层、由作者实机看着调。
    ///
    /// 一条硬约束仍然管着它：**像素只许全透明或全不透明**，所以 alpha 要么 0 要么 1，
    /// 不要拿半透明去表达「不可用」（那会在屏幕上留下插值像素）。
    /// </remarks>
    [Export]
    public Color UsableColor { get; set; } = Colors.White;

    /// <summary>够不到时框的颜色。要与够得到那一种当场分得开。</summary>
    [Export]
    public Color BlockedColor { get; set; } = Colors.White;

    /// <summary>框线宽，世界像素。</summary>
    [Export]
    public float OutlineWidthPx { get; set; } = 1f;

    /// <summary>
    /// 玩家这一下会作用在哪一格。**这是全部逐格动作唯一的读口。**
    /// </summary>
    public Vector2I Cell { get; private set; }

    /// <summary>那一格够不够得到。够不到时动作要被拒，而框仍然画、只是画成不可用。</summary>
    public bool IsWithinReach { get; private set; }

    private TileMapLayer _grid = null!;
    private FarmWalker _player = null!;
    private InputRouter _router = null!;

    public override void _Ready()
    {
        _grid = GridLayer ?? throw new InvalidOperationException(
            $"{nameof(FarmCellCursor)}（节点 {Name}）的 {nameof(GridLayer)} 没接上 —— "
                + "它要靠一层 TileMapLayer 把世界坐标换成格坐标");

        _player = Player ?? throw new InvalidOperationException(
            $"{nameof(FarmCellCursor)}（节点 {Name}）的 {nameof(Player)} 没接上 —— "
                + "两个设备族都要读角色站在哪一格，手柄那一侧还要读他朝哪一向");

        _router = Router ?? throw new InvalidOperationException(
            $"{nameof(FarmCellCursor)}（节点 {Name}）的 {nameof(Router)} 没接上 —— "
                + "它要靠门面判断最后用的是键鼠还是手柄");

        if (_grid.TileSet is null)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmCellCursor)} 接的那一层（{_grid.Name}）还没有 TileSet —— "
                + "没有 TileSet 就没有格大小，框画不出来");
        }

        if (ReachInCells < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmCellCursor)}（节点 {Name}）的 {nameof(ReachInCells)} 是 {ReachInCells} —— "
                    + "这个量没有默认值，要在检查器里填一个至少为 1 的数"
                    + "（它归 `GP-6` 实测调；形状是十字、不含斜角，本项只定伸多远）");
        }
    }

    public override void _Process(double delta)
    {
        Vector2I playerCell = CellOf(_player.GlobalPosition);
        Vector2I target = _router.Device == InputDeviceKind.Gamepad
            ? playerCell + _player.Facing
            : CellOf(GetGlobalMousePosition());

        var reachable = WithinReach(target - playerCell);

        // 只在真的变了才重画：_Draw 每帧都跑一遍是白算的，而这一格多数帧不动。
        if (target != Cell || reachable != IsWithinReach)
        {
            Cell = target;
            IsWithinReach = reachable;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2I tileSize = _grid.TileSet.TileSize;
        Vector2 center = ToLocal(_grid.ToGlobal(_grid.MapToLocal(Cell)));
        var rect = new Rect2(center - ((Vector2)tileSize / 2f), tileSize);

        // filled: false —— 只勾边。整格高亮会盖住那一格上的作物（见类注释第三条）。
        DrawRect(rect, IsWithinReach ? UsableColor : BlockedColor, filled: false, OutlineWidthPx);
    }

    /// <summary>某个世界坐标落在哪一格。</summary>
    private Vector2I CellOf(Vector2 globalPosition) =>
        _grid.LocalToMap(_grid.ToLocal(globalPosition));

    /// <summary>
    /// 相对角色的那个偏移够不够得到。**十字形：斜角一律够不到。**
    /// </summary>
    private bool WithinReach(Vector2I delta) =>
        (delta.X == 0 || delta.Y == 0)
        && Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) <= ReachInCells;
}
