using Godot;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>
/// 当前操作格的光标：算出玩家这一下会作用在哪一格，并给那一格画一个框。
/// </summary>
/// <remarks>
/// 键鼠时取鼠标指的那一格，手柄时取角色身前那一格。两条算法只写在这里，外面统一读
/// <see cref="Cell"/>，所以以后再加逐格动作（锄、播、浇、收、建造摆位）不用分设备各接一遍。
///
/// 够不到的格也画框，只是换成 <see cref="BlockedColor"/>。一律不画的话，玩家按下去没反应
/// 却看不出原因。
///
/// 可及范围是十字形，斜角一律够不到。原因不是距离，是角色只有上下左右四个朝向：指向斜角他
/// 没有朝向可转，画面上会变成身子朝正面、手在斜着锄。
///
/// 规则出自设计仓 design/界面系统.md 的「世界里那一层：当前操作格」一节。
/// </remarks>
public partial class FarmCellCursor : Node2D
{
    /// <summary>做世界坐标与格坐标换算用的瓦片层。</summary>
    /// <remarks>
    /// 必须和 <see cref="FarmField"/> 接的是同一层（也就是耕地层 Tilled）。接了格子大小或位置
    /// 不同的另一层时，框会落在这一格而真正作用的是旁边那一格，引擎不报错。
    /// </remarks>
    [Export]
    public TileMapLayer? GridLayer { get; set; }

    /// <summary>角色。要读他站在哪一格；手柄时还要读他的朝向。</summary>
    [Export]
    public FarmWalker? Player { get; set; }

    /// <summary>输入门面。这里只用它判断玩家最后用的是键鼠还是手柄。</summary>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>可及范围伸多远，按格数算。形状固定是十字，这个值只决定长度。</summary>
    /// <remarks>
    /// 没有默认值，必须在检查器里填，至少 1（1 就是脚下那一格加上下左右四格）。
    /// 填多少要实机试：太小则鼠标指偏一点就够不到，太大则角色看起来手伸得过长。
    /// 定下来之后登进设计仓 design/数值模型.md。
    /// </remarks>
    [Export]
    public int ReachInCells { get; set; }

    /// <summary>够得到时框的颜色。</summary>
    /// <remarks>
    /// alpha 要么 0 要么 1。像素风下半透明会在屏幕上留下插值出来的中间色，和最近邻过滤、
    /// 整数缩放对不上。颜色本身由作者在检查器里调，所以不走 rules/UI/HudPalette.cs
    /// —— 那份是 HUD 的色板，这个框在世界里。
    /// </remarks>
    [Export]
    public Color UsableColor { get; set; } = Colors.White;

    /// <summary>够不到时框的颜色。要和 <see cref="UsableColor"/> 一眼分得开。</summary>
    [Export]
    public Color BlockedColor { get; set; } = Colors.White;

    /// <summary>框线宽，单位是世界像素。</summary>
    [Export]
    public float OutlineWidthPx { get; set; } = 1f;

    /// <summary>玩家这一下会作用在哪一格。所有逐格动作都读这个属性。</summary>
    public Vector2I Cell { get; private set; }

    /// <summary>
    /// <see cref="Cell"/> 够不够得到。够不到时动作要被拒，但框照画，只是换成不可用的颜色。
    /// </summary>
    public bool IsWithinReach { get; private set; }

    // 下面三个是 _Ready 里校验过的非空副本，省掉每帧的判空。
    private TileMapLayer _grid = null!;
    private FarmWalker _player = null!;
    private InputRouter _router = null!;

    public override void _Ready()
    {
        // 先停掉每帧回调，全部校验过了再开。_Ready 抛出之后引擎并不会停掉这个节点，_Process
        // 照旧每帧跑而下面那几个字段还是 null —— 于是说清缺了什么的那一条会被成百条空引用刷走。
        SetProcess(false);

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
                    + "（形状固定是十字、不含斜角，这个数只决定伸多远，填多少实机试）");
        }

        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        Vector2I playerCell = CellOf(_player.GlobalPosition);
        Vector2I target = _router.Device == InputDeviceKind.Gamepad
            ? playerCell + _player.Facing
            : CellOf(GetGlobalMousePosition());

        var reachable = WithinReach(target - playerCell);

        // 只在格子或可及状态真的变了才重画。这一格多数帧都不动，每帧重画是白算的。
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

        // filled: false 就是只勾边。整格填色会盖住格子上的作物，而「这一格能不能收」要靠看作物。
        DrawRect(rect, IsWithinReach ? UsableColor : BlockedColor, filled: false, OutlineWidthPx);
    }

    /// <summary>某个世界坐标落在哪一格。</summary>
    private Vector2I CellOf(Vector2 globalPosition) =>
        _grid.LocalToMap(_grid.ToLocal(globalPosition));

    /// <summary>
    /// 相对角色的偏移够不够得到。十字形的判法：两个分量里有一个是 0（排除斜角），
    /// 且两者绝对值之和不超过 <see cref="ReachInCells"/>。
    /// </summary>
    private bool WithinReach(Vector2I delta) =>
        (delta.X == 0 || delta.Y == 0)
        && Mathf.Abs(delta.X) + Mathf.Abs(delta.Y) <= ReachInCells;
}
