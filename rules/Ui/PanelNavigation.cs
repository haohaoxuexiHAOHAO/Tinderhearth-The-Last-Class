namespace Tinderhearth.Rules.Ui;

/// <summary>面板在手柄上怎么导航。</summary>
public enum PanelNavigationMode
{
    /// <summary>焦点移动：方向键把焦点挪到相邻控件。列表、标签页、按钮组用它。</summary>
    Focus,

    /// <summary>光标移动：方向键挪一个格子光标，确认键才落子。网格摆放用它。</summary>
    Cursor,
}

/// <summary>
/// 面板导航范式（`UI-7`）。**两种，按面板形状选，不由各面板自己发明。**
/// </summary>
/// <remarks>
/// 这不是偏好而是有客观优劣的技术选择（依据写在 PRD 的 `US-006`）：
///
/// **列表与标签页走焦点移动。** 目标是离散的、数量是十来个，焦点落点就是控件本身。Godot 内置的
/// `ui_up`／`ui_down`／`ui_left`／`ui_right` 加上控件的 <c>FocusMode</c> 直接就能用，不写代码 ——
/// 已经在 `UI-6` 的手环标签页上落地。
///
/// **网格摆放走光标移动。** 基地可建造区是 40×30 格（正典的场景规模，是否扩大已记 `GP-8`），
/// 用焦点导航就得为 1200 个格子各建一个可聚焦控件并连好邻居链，而且**摆放需要的是一个坐标而不是
/// 一个控件** —— 建筑占多格时落点是左上角格，焦点这个概念表达不了。
///
/// 光标**钳制在边界内，不环绕**。环绕会让镜头突然横跨整张地图：正典要求相机行为可预期，而建造
/// 时相机跟着光标滚动并在接近边缘时推镜（PRD 的 `FR-23`）。
/// </remarks>
public static class PanelNavigation
{
    /// <summary>
    /// 走光标移动的手环标签页。其余一律走焦点移动。
    /// </summary>
    /// <remarks>
    /// 只列 id 不列理由，因为理由只有一条且对所有成员相同：它是网格摆放。有测试盯着这里的每个 id
    /// 都真的是手环的标签页 —— 拼错的话它会静默退回焦点导航，而那在实机上看起来只是「有点难用」。
    /// </remarks>
    public static readonly IReadOnlyList<string> CursorNavigatedTabs = ["build"];

    /// <summary>某个标签页该用哪种导航。</summary>
    public static PanelNavigationMode ModeFor(string tabId) =>
        CursorNavigatedTabs.Contains(tabId) ? PanelNavigationMode.Cursor : PanelNavigationMode.Focus;
}

/// <summary>
/// 网格摆放用的光标（`UI-7`）。**尺寸由调用方传入，不写死。**
/// </summary>
/// <remarks>
/// 不写死 40×30 是 PRD 的 `FR-24`（可建造区尺寸必须从配置读）。这里连默认值都不给 —— 给了默认值
/// 就会有人省掉传参，而那正是「写死一个数字」的另一种形态。
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
    /// 按方向挪一格。**钳制在边界内**，返回光标是否真的动了。
    /// </summary>
    /// <remarks>
    /// 返回值不是摆设：贴边时继续推方向应该**没有反馈音也不推镜**，否则玩家会以为镜头卡住。
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

    /// <summary>跳到指定格。越界就抛 —— 静默钳制会把「算错了坐标」藏起来。</summary>
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
