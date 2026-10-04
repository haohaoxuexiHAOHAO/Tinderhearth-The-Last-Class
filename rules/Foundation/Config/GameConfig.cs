using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Foundation.Config;

/// <summary>
/// 从数据文件读来的配置。代码里不出现这些数字的字面量。
/// </summary>
/// <remarks>
/// 这里只放「结构性容量」这一类现在就确定要外置的量。玩法数值一律不进来 —— 属性公式、成长
/// 曲线、价格与消耗量在设计仓 design/数值模型.md。相机手感（死区、震动幅度、推镜速度）也不
/// 进来，它是表现规则而不是内容，写在 <c>rules/UI/CameraFeel.cs</c>。
///
/// 每个字段都在构造时校验为正。这不是防御性代码，而是补一个真实的静默失效：位置参数
/// <c>record</c> 配 <c>System.Text.Json</c> 时，JSON 里缺字段不报错，会拿 <c>default(int)</c>
/// 也就是 0 填进来（有测试实测这条），而 0 名册容量表现为谁都招不进来，游戏不报错。
/// </remarks>
/// <param name="RosterCapacity">
/// 名册容量。玩法正典说第一版的学生陆续到齐，但容量必须从配置读而不是写死 —— 它是 mod 与
/// 未来联机的共同地基：mod 加角色、联机加玩家，都会撞这个数。
/// </param>
/// <param name="BuildableWidthCells">
/// 基地可建造区的列数。格数本身写在设计仓 canon/gameplay/时间与经营.md 的「建造」一节，这里
/// 不复述那个数、也不许写死：相机的滚动范围与边缘推镜都按它算，改画布应该是改这个配置值加
/// 延伸地图，不是改代码。
/// </param>
/// <param name="BuildableHeightCells">基地可建造区的行数。理由同上。</param>
public sealed record GameConfig(
    int RosterCapacity,
    int BuildableWidthCells,
    int BuildableHeightCells)
{
    public const string ContentPath = "config/game.json";

    /// <inheritdoc cref="GameConfig(int, int, int)"/>
    public int RosterCapacity { get; } = Positive(RosterCapacity, nameof(RosterCapacity));

    /// <inheritdoc cref="GameConfig(int, int, int)"/>
    public int BuildableWidthCells { get; } =
        Positive(BuildableWidthCells, nameof(BuildableWidthCells));

    /// <inheritdoc cref="GameConfig(int, int, int)"/>
    public int BuildableHeightCells { get; } =
        Positive(BuildableHeightCells, nameof(BuildableHeightCells));

    public static GameConfig Parse(string json) =>
        ContentJson.Parse<GameConfig>(json, ContentPath);

    private static int Positive(int value, string field) => value > 0
        ? value
        : throw new ArgumentOutOfRangeException(
            field, $"{ContentPath} 的 {field} 必须为正，实际 {value}（字段缺失也会得到 0）");
}
