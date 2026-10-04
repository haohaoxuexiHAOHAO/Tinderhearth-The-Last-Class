namespace Tinderhearth.Rules.Economy;

/// <summary>按下播种那一下的结果。</summary>
/// <remarks>
/// 三条被拒路径各有自己的取值，不合并成一个「失败」。合并之后玩家按下去没反应，而界面说不出是
/// 哪一种，可这三种的补救办法完全不同：换一格选中、换一样东西拿、先锄地。
/// </remarks>
public enum PlantResult
{
    /// <summary>播下去了。</summary>
    Planted,

    /// <summary>手上那一格是空的 —— 他没拿东西。</summary>
    NothingSelected,

    /// <summary>手上拿着东西，但那样东西不是种子。</summary>
    NotASeed,

    /// <summary>这一格现在不许播（不是已锄的地）。</summary>
    CellNotReady,
}

/// <summary>
/// 把「玩家手上那一格」与「他对着的那一格地」接起来。
/// </summary>
/// <remarks>
/// 这一层存在是为了让那三条拒绝路径各说得出是哪一种。<see cref="Plot.Plant"/> 只返回
/// <c>bool</c>，它答得了「这一格许不许播」，答不了「他手上那样东西能不能播」—— 而后者是两种
/// 不同的情况：空手，以及拿着一样不是种子的东西。
///
/// 它不知道随身栏。传进来的是一个物品标识（空手传 <c>null</c> 或空串），所以规则层的经营这一侧
/// 不依赖界面那一侧 —— 随身栏在 <c>rules/UI/CarrySlots.cs</c>，两边靠调用方接起来。
///
/// 它也不知道什么算种子。那要查物品定义，而物品定义是数据文件里的内容、会被 mod 换掉。所以
/// 「这个标识是哪一种作物的种子」由调用方给的解析器回答，答不出来就算不是种子。
/// </remarks>
public static class Planting
{
    /// <summary>
    /// 拿手上那一格去播这一格地。
    /// </summary>
    /// <param name="plot">对着的那一格地。</param>
    /// <param name="selectedItemId">手上那一格的物品标识；空手传 <c>null</c> 或空串。</param>
    /// <param name="resolveSeed">
    /// 拿标识换一份作物定义；它不是种子就返回 <c>null</c>。
    /// </param>
    /// <remarks>
    /// 判的顺序是先看手上、再看地里。玩家按下去那一刻，动作是按格子状态派发的（已锄的格才派发
    /// 到播种），所以「这一格不许播」在游戏里到不了，只在规则层被测到；手上那两条排在前面，
    /// 玩家拿到的就是他真能改的那一条。
    ///
    /// 成功那一下真的改了 <paramref name="plot"/> 的状态，本方法不是一个只判不做的检查 ——
    /// 分成「先问能不能、再真的播」两步会让两处条件分叉，而分叉时只表现为「按了没反应」。
    /// </remarks>
    public static PlantResult TryPlant(
        Plot plot, string? selectedItemId, Func<string, CropDefinition?> resolveSeed)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(resolveSeed);

        if (string.IsNullOrEmpty(selectedItemId))
        {
            return PlantResult.NothingSelected;
        }

        if (resolveSeed(selectedItemId) is not CropDefinition crop)
        {
            return PlantResult.NotASeed;
        }

        return plot.Plant(crop) ? PlantResult.Planted : PlantResult.CellNotReady;
    }
}
