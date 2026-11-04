namespace Tinderhearth.Rules.Foundation.Terrain;

/// <summary>一个 8 位的 RGB 颜色，比较时三个通道逐位相等。</summary>
/// <remarks>不用浮点颜色：贴图里的值与色板里的值本该逐位相同，转成整数之后才比得准。</remarks>
public readonly record struct Rgb8(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>数据层地形集里的一种地形：编号、名字，以及从双网格图里读出来的主色。</summary>
/// <param name="Terrain">地形编号，从 0 起算。</param>
/// <param name="Name">它在 TileSet 里叫什么，报错时给作者认。</param>
/// <param name="Key">它的主色，从认得它的那一套双网格图里四角全同那一张读出来；没有哪一套认得它时是 null。</param>
public sealed record FillTerrain(int Terrain, string Name, Rgb8? Key);

/// <summary>检查器里纹理声明数组的一项：管哪种地形、给了纹理图还是写了不铺。</summary>
/// <remarks>由引擎层从那一项资源读出来。规则层看不见贴图，只看给没给、多大。</remarks>
/// <param name="Index">它是数组里的第几项，从 0 起算。</param>
/// <param name="Empty">那一格是空的，连资源都没建。</param>
/// <param name="Terrain">它声明的地形编号；没填是 -1。</param>
/// <param name="HasTexture">给没给纹理图。</param>
/// <param name="NoFill">勾没勾「不铺」。</param>
/// <param name="Width">纹理图多宽，单位像素；没给图时是 0。</param>
/// <param name="Height">纹理图多高。</param>
public sealed record FillDeclaration(int Index, bool Empty, int Terrain, bool HasTexture, bool NoFill, int Width, int Height);

/// <summary>天然地形的纹理声明配得对不对：载入时核一次，几处不对一次全报。</summary>
/// <remarks>
/// 显示层里一种地形的主色会换成它那张纹理图上同一世界位置的像素，规矩在设计仓 production/场景绘制约定.md 的「双网格」一节。
/// 一项都没声明是合法的：那是不铺纹理，地面照双网格图原样画，所以返回空表。声明了任意一项，就要每种地形都有一项，
/// 不然「这片有纹理、那片没有」分不出是故意的还是漏了。
///
/// 报的顺序固定：先按项号报每一项自己的错，再报声明了两次的、没声明的，最后报主色撞了的。
/// </remarks>
public static class TerrainFillCheck
{
    /// <summary>核纹理声明。全对、或一项都没声明时返回空表。</summary>
    /// <param name="terrains">数据层地形集里的每一种地形，按编号排。</param>
    /// <param name="fills">检查器里那个数组的每一项，按项号排。</param>
    /// <param name="cellSize">格边长，单位像素。纹理图的宽高都要是它的整数倍，铺开才与格子对得齐。</param>
    public static IReadOnlyList<string> Problems(
        IReadOnlyList<FillTerrain> terrains, IReadOnlyList<FillDeclaration> fills, int cellSize)
    {
        ArgumentNullException.ThrowIfNull(terrains);
        ArgumentNullException.ThrowIfNull(fills);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cellSize, 0);

        var problems = new List<string>();
        if (fills.Count == 0)
        {
            return problems;
        }

        var byId = terrains.ToDictionary(terrain => terrain.Terrain);
        foreach (var fill in fills)
        {
            problems.AddRange(OwnProblems(fill, byId, cellSize));
        }

        var declared = fills
            .Where(fill => !fill.Empty && byId.ContainsKey(fill.Terrain))
            .GroupBy(fill => fill.Terrain)
            .ToDictionary(group => group.Key, group => group.ToList());
        foreach (var (id, list) in declared.OrderBy(pair => pair.Key).Where(pair => pair.Value.Count > 1))
        {
            problems.Add($"{Label(byId[id])}声明了 {list.Count} 次：第 {string.Join("、", list.Select(fill => fill.Index))} 项"
                + " —— 同一种地形只留一项，多半是复制了一项忘了改 Terrain");
        }

        foreach (var terrain in terrains.Where(terrain => !declared.ContainsKey(terrain.Terrain)))
        {
            problems.Add($"{Label(terrain)}没有声明：要么给它配一张纹理图，要么加一项勾上 No Fill"
                + " —— 只要声明了任意一种，每种地形都要有一项，不然分不出是故意不铺还是漏了");
        }

        var textured = declared.Where(pair => pair.Value.Any(fill => fill.HasTexture)).Select(pair => pair.Key).ToHashSet();
        var sameKey = terrains
            .Where(terrain => terrain.Key is not null)
            .GroupBy(terrain => terrain.Key!.Value)
            .Where(group => group.Count() > 1 && group.Any(terrain => textured.Contains(terrain.Terrain)));
        foreach (var group in sameKey)
        {
            problems.Add($"{string.Join("与", group.Select(Label))}的主色都是 {group.Key}：换纹理时分不清是哪一种，"
                + "两种都会铺上同一张 —— 双网格图里这几种地形要用不同的主色");
        }

        return problems;
    }

    /// <summary>一项自己就看得出来的错：空着、没填地形、地形不存在、纹理图与不铺没二选一、尺寸不是整格、读不出主色。</summary>
    private static IEnumerable<string> OwnProblems(FillDeclaration fill, Dictionary<int, FillTerrain> byId, int cellSize)
    {
        var at = $"第 {fill.Index} 项";
        if (fill.Empty)
        {
            yield return $"{at}是空的：点那一格的下拉，新建一个 TerrainFill";
            yield break;
        }

        if (fill.Terrain < 0)
        {
            yield return $"{at}没填 Terrain：填数据层地形集里的地形编号，从 0 起算";
            yield break;
        }

        if (!byId.TryGetValue(fill.Terrain, out var terrain))
        {
            var known = string.Join("、", byId.Values.OrderBy(t => t.Terrain).Select(t => $"{t.Terrain} {t.Name}"));
            yield return $"{at}的 Terrain 是 {fill.Terrain}，数据层的地形集里没有这一种（有的是 {known}）";
            yield break;
        }

        var who = $"{at}（{Label(terrain)}）";
        if (fill.HasTexture && fill.NoFill)
        {
            yield return $"{who}既给了纹理图又勾了 No Fill：二选一，要铺就取消勾选 No Fill，不铺就把纹理图清掉";
        }
        else if (!fill.HasTexture && !fill.NoFill)
        {
            yield return $"{who}既没给纹理图也没勾 No Fill：要铺就把纹理图拖进 Texture，不铺就勾上 No Fill";
        }

        if (fill.HasTexture && (fill.Width % cellSize != 0 || fill.Height % cellSize != 0))
        {
            yield return $"{who}的纹理图是 {fill.Width}×{fill.Height}：宽和高都要是格边长 {cellSize} 的整数倍，"
                + "铺开才与格子对得齐";
        }

        if (fill.HasTexture && terrain.Key is null)
        {
            yield return $"{who}给了纹理图，可没有哪一套双网格图认得这种地形，读不出它的主色 —— 先在 Pairs 里给它配一套";
        }
    }

    private static string Label(FillTerrain terrain) => $"地形「{terrain.Name}」（编号 {terrain.Terrain}）";
}
