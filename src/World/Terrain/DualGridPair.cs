using Godot;

namespace Tinderhearth.World.Terrain;

/// <summary>
/// 一对地形的双网格素材：16 张图，回答「我四个角下面压着的各是什么」。
/// </summary>
/// <remarks>
/// 一套管一对地形，不是管一种。会挨着的每一对各画一套，对方那一片直接画在图里。所以基地要
/// 草与裸土、草与水两套，而裸土与水那一对靠「让它们之间总隔一圈草」这条地图纪律省掉。
///
/// 两种地形谁记作 0 谁记作 1 由这份资源声明，填反了整套图会镜像地拼错，而引擎不报错。判法是
/// 看图集里四角全同的那两张：全是 <see cref="TerrainA"/> 的那一张编码为 0000。
///
/// 画法出自设计仓 production/场景绘制约定.md 的「双网格」一节。
/// </remarks>
[GlobalClass]
public partial class DualGridPair : Resource
{
    /// <summary>编码里记作 0 的那种地形，填数据层地形集里的地形编号。</summary>
    /// <remarks>
    /// 初值是 -1 而不是 0，因为地形编号从 0 起算，0 是一个合法编号。-1 表示「没填」，
    /// 不填过不了校验。
    /// </remarks>
    [Export]
    public int TerrainA { get; set; } = -1;

    /// <summary>编码里记作 1 的那种地形。</summary>
    [Export]
    public int TerrainB { get; set; } = -1;

    /// <summary>这 16 张在显示层 TileSet 里是第几个图集源。</summary>
    [Export]
    public int DisplaySourceId { get; set; }

    /// <summary>那 16 张在图集里的左上角格坐标。整套按 4×4 排布，所以从这里往右下数四格。</summary>
    [Export]
    public Vector2I AtlasOrigin { get; set; }

    /// <summary>这一对认不认得某个地形编号。</summary>
    public bool Covers(int terrain) => terrain == TerrainA || terrain == TerrainB;

    /// <summary>把一个地形编号折成编码里的一位。</summary>
    public int BitOf(int terrain) => terrain == TerrainB ? 1 : 0;

    /// <summary>缺声明就抛，并说清缺哪一项。</summary>
    public void RequireConfigured(int index)
    {
        if (TerrainA < 0 || TerrainB < 0)
        {
            throw new InvalidOperationException(
                $"第 {index} 份 {nameof(DualGridPair)} 的 {nameof(TerrainA)}／{nameof(TerrainB)} "
                    + $"是 {TerrainA}／{TerrainB}，两个都要填数据层地形集里的地形编号（从 0 起算）");
        }

        if (TerrainA == TerrainB)
        {
            throw new InvalidOperationException(
                $"第 {index} 份 {nameof(DualGridPair)} 的两种地形都是 {TerrainA}。"
                    + "一套双网格管的是两种地形之间的交接，两边填同一个没有意义");
        }
    }
}
