namespace Tinderhearth.Rules.UI;

/// <summary>面板在手柄上怎么导航。</summary>
public enum PanelNavigationMode
{
    /// <summary>焦点移动：方向键把焦点挪到相邻控件。列表、标签页、按钮组用它。</summary>
    Focus,

    /// <summary>光标移动：方向键挪一个格子光标，确认键才落子。网格摆放用它。</summary>
    Cursor,
}

/// <summary>
/// 面板导航范式。按这一页的目标是离散控件还是格子来选，不由各面板自己发明。
/// </summary>
/// <remarks>
/// 列表与标签页走焦点移动：目标是离散的、数量有限，焦点落在哪就是落在哪个控件上。Godot 内置的
/// <c>ui_up</c> 那一组动作加上控件的 <c>FocusMode</c> 直接就能用，不用写代码。
///
/// 网格摆放走光标移动。用焦点导航就得给每个格子各建一个可聚焦控件并连好邻居链，而格数由配置给、
/// 可以很大；更要紧的是摆放需要的是一个坐标 —— 建筑占多格时落点是左上角那一格，焦点表达不了。
///
/// 光标钳制在边界内，不环绕。环绕会让镜头突然横跨整张地图，而建造时相机跟着光标滚动、接近边缘
/// 还会推镜，这种情况下镜头必须是可预期的。
/// </remarks>
public static class PanelNavigation
{
    /// <summary>
    /// 走光标移动的手环标签页。其余一律走焦点移动。
    /// </summary>
    /// <remarks>
    /// id 拼错不会报错，只会静默退回焦点移动，而那在实机上看起来只是「这一页有点难用」。
    /// 所以有测试盯着这里每个 id 都真的是手环的标签页。
    /// </remarks>
    public static readonly IReadOnlyList<string> CursorNavigatedTabs = ["build"];

    /// <summary>某个标签页该用哪种导航。</summary>
    public static PanelNavigationMode ModeFor(string tabId) =>
        CursorNavigatedTabs.Contains(tabId) ? PanelNavigationMode.Cursor : PanelNavigationMode.Focus;
}

/// <summary>
/// 网格摆放用的光标。网格尺寸由调用方传入，不写死。
/// </summary>
/// <remarks>
/// 可建造区多大要从配置读。这里连默认值都不给 —— 给了就会有人省掉传参，
/// 而那与写死一个数字是一回事。
/// </remarks>
public sealed class GridCursor
{
    /// <summary>建一个光标，初始停在左上角。</summary>
    /// <param name="width">网格列数。</param>
    /// <param name="height">网格行数。</param>
    public GridCursor(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"网格尺寸必须为正：{width}×{height}");
        }

        Width = width;
        Height = height;
    }

    /// <summary>网格列数。</summary>
    public int Width { get; }

    /// <summary>网格行数。</summary>
    public int Height { get; }

    /// <summary>光标所在列，0 起。</summary>
    public int Column { get; private set; }

    /// <summary>光标所在行，0 起。</summary>
    public int Row { get; private set; }

    /// <summary>
    /// 按方向挪一格。钳制在边界内，返回光标是否真的动了。
    /// </summary>
    /// <remarks>
    /// 返回值不是摆设：贴边时继续推方向应该没有反馈音、也不推镜，否则玩家会以为镜头卡住。
    /// </remarks>
    public bool Move(int deltaColumn, int deltaRow)
    {
        var col = Math.Clamp(Column + deltaColumn, 0, Width - 1);
        var row = Math.Clamp(Row + deltaRow, 0, Height - 1);
        if (col == Column && row == Row)
        {
            return false;
        }

        Column = col;
        Row = row;
        return true;
    }

    /// <summary>跳到指定格。越界就抛 —— 静默钳制会把「坐标算错了」藏起来。</summary>
    public void MoveTo(int column, int row)
    {
        if (column < 0 || column >= Width || row < 0 || row >= Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(column), $"格子 ({column},{row}) 不在 {Width}×{Height} 的网格里");
        }

        Column = column;
        Row = row;
    }
}
