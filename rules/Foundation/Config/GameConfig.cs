using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Foundation.Config;

/// <summary>
/// 从数据文件读来的配置。**代码里不出现这些数字的字面量**（`ENG-5`）。
/// </summary>
/// <remarks>
/// 这里只放「结构性容量」这类现在就确定要外置的量。**玩法数值一律不进这里** ——
/// 属性公式、成长曲线、判定公式、价格与消耗量归 `GP-2`，它还没设计，现在写进来就是
/// 把猜出来的数字伪装成配置。
/// </remarks>
/// <param name="RosterCapacity">
/// 名册容量。玩法正典说第一版九名学员陆续到齐，但**容量必须从配置读而不是写死 9** ——
/// 它是 mod 与未来联机的共同地基：mod 加角色、联机加玩家，都会撞这个数。
/// </param>
public sealed record GameConfig(int RosterCapacity)
{
    public const string ContentPath = "config/game.json";

    public static GameConfig Parse(string json) =>
        ContentJson.Parse<GameConfig>(json, ContentPath);
}
