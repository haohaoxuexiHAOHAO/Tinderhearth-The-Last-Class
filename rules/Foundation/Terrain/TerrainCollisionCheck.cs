namespace Tinderhearth.Rules.Foundation.Terrain;

/// <summary>数据层 TileSet 里的一张瓦片：标在哪个地形集的哪种地形上、是哪一张、带不带碰撞。</summary>
/// <remarks>由引擎层从 TileSet 读出来。规则层看不见图集与多边形，只看它标了什么、有没有碰撞。</remarks>
/// <param name="TerrainSet">它标在第几个地形集上；没标是 -1。</param>
/// <param name="Terrain">它标的地形编号；没标是 -1。</param>
/// <param name="TerrainName">那种地形在 TileSet 里叫什么，报错时给作者认。</param>
/// <param name="Tile">它是哪一张，写成作者在 TileSet 面板里认得出的样子：哪张图、哪一格。</param>
/// <param name="HasCollision">它在任何一个物理层上有没有碰撞形状。</param>
public sealed record TerrainTile(int TerrainSet, int Terrain, string TerrainName, string Tile, bool HasCollision);

/// <summary>数据层的碰撞配得对不对：载入时核一次，几处不对一次全报。</summary>
/// <remarks>
/// 俯视里地形挡人靠的是数据层标记瓦片上的碰撞，理由在设计仓 production/场景绘制约定.md 的「俯视里什么挡人」一节。
/// 这里核的三种配法都会让水悄悄变得能走而不报错：那一层被停用（Enabled 关掉的是整层，碰撞也在内）、那一层的碰撞
/// 被关掉、同一种地形的几张标记瓦片有的带碰撞有的不带（用不带的那几张刷的格子走得进去）。
///
/// 哪几种地形该挡人这里判不了：代码不知道哪一种是水，那是作者给哪几张画碰撞时定的。
/// </remarks>
public static class TerrainCollisionCheck
{
    /// <summary>核数据层。全对返回空表；先报那一层的两个开关，再按地形编号报碰撞不齐的那几种。</summary>
    /// <param name="layerEnabled">数据层的 Enabled 开着没有。</param>
    /// <param name="collisionEnabled">数据层的 Collision Enabled 开着没有。</param>
    /// <param name="dataTerrainSet">数据层用的是第几个地形集。别的地形集上的瓦片不参与。</param>
    /// <param name="tiles">数据层 TileSet 里的每一张瓦片，顺序照 TileSet 里的顺序。</param>
    public static IReadOnlyList<string> Problems(
        bool layerEnabled, bool collisionEnabled, int dataTerrainSet, IReadOnlyList<TerrainTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        var problems = new List<string>();
        if (!layerEnabled)
        {
            problems.Add("数据层的 Enabled 关着：地形的碰撞挂在这一层的标记瓦片上，关掉 Enabled 碰撞跟着没了，水就走得进去"
                + " —— 把 Enabled 勾回来；要藏起这一层，点场景面板里它那只眼睛（只关 Visible，碰撞还在）");
        }

        if (!collisionEnabled)
        {
            problems.Add("数据层的 Collision Enabled 关着（检查器 Physics 那一组）：标记瓦片上画的碰撞不起作用，水就走得进去"
                + " —— 勾回来");
        }

        var marked = tiles
            .Where(tile => tile.TerrainSet == dataTerrainSet && tile.Terrain >= 0)
            .GroupBy(tile => tile.Terrain)
            .OrderBy(group => group.Key);
        foreach (var group in marked)
        {
            string[] with = [.. group.Where(tile => tile.HasCollision).Select(tile => tile.Tile)];
            string[] without = [.. group.Where(tile => !tile.HasCollision).Select(tile => tile.Tile)];
            if (with.Length > 0 && without.Length > 0)
            {
                problems.Add($"地形「{group.First().TerrainName}」（编号 {group.Key}）的标记瓦片碰撞不齐：带碰撞的是"
                    + $" {string.Join("、", with)}，不带的是 {string.Join("、", without)} —— 用不带的那几张刷的格子走得进去；"
                    + "同一种地形要么都画碰撞、要么都不画，在数据层那份 TileSet 里把它们改成一样");
            }
        }

        return problems;
    }
}
