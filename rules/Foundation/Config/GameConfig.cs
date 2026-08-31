using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Foundation.Config;

/// <summary>
/// 从数据文件读来的配置。**代码里不出现这些数字的字面量**（`ENG-5`）。
/// </summary>
/// <remarks>
/// 这里只放「结构性容量」这类现在就确定要外置的量。**玩法数值一律不进这里** ——
/// 属性公式、成长曲线、判定公式、价格与消耗量的设计在设计仓 `design/数值模型.md`，
/// 把它们搬进代码属各玩法实现需求，不是往这份配置里加字段。相机手感（死区、震动幅度、推镜速度）
/// 同样不进这里：那是表现规则，正典的「内容外置，规则不外置」把它划在外面，
/// 落点是 `rules/Ui/CameraFeel.cs`。
///
/// **每个字段都在构造时校验为正。** 这不是防御性代码，是补一个真实的静默失效：位置参数
/// <c>record</c> 配 <c>System.Text.Json</c> 时，JSON 里**缺字段不报错**，会拿
/// <c>default(int)</c> 也就是 0 填进来（有测试实测这条）。0 名册容量表现为「谁都招不进来」，
/// 0 格可建造区表现为「相机钳制退化、建造区不存在」—— 两者都不报错，只是游戏不对。
/// </remarks>
/// <param name="RosterCapacity">
/// 名册容量。玩法正典说第一版九名学员陆续到齐，但**容量必须从配置读而不是写死 9** ——
/// 它是 mod 与未来联机的共同地基：mod 加角色、联机加玩家，都会撞这个数。
/// </param>
/// <param name="BuildableWidthCells">
/// 基地可建造区的列数。正典定 40×30 格（640×480px，见[时间与经营 · 建造]），**不得写死**
/// （PRD 的 `FR-24`）：相机的滚动范围与边缘推镜都按它算，而是否扩大已记 `GP-8` ——
/// 将来扩大就该是改这个数加延伸地图，不是改代码。
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
