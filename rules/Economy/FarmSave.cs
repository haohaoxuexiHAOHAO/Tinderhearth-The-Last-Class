using Tinderhearth.Rules.Foundation.Save;

namespace Tinderhearth.Rules.Economy;

/// <summary>农场那一份正式存档：读回或者开新档，以及写成一份文本。读写文件那一下归引擎层。</summary>
/// <remarks>
/// 读不出来、被拒、或者存档里那片地与场景里那片地对不上，一律抛出去，不开新档：开了新档，下一次睡觉就会把那份
/// 读不出来的存档盖掉，而它多半只是装少了一个 mod 或者改过一次场景。
/// </remarks>
public static class FarmSave
{
    /// <summary>一个列表里最多点名几格，再多就只报个数。</summary>
    private const int NamedCellLimit = 12;

    /// <summary>有存档就读回，没有就按开局那一天、场景里那片地与空背包开新档。</summary>
    /// <param name="text">存档文件的全文；没有存档时为 <c>null</c>。</param>
    /// <param name="registry">已注册世界那几个分片的那一份（<see cref="WorldShards.Register"/>）。</param>
    /// <param name="crops">按标识找作物定义。</param>
    /// <param name="sceneField">场景里那片地的起始状态。</param>
    /// <param name="newGameBag">开新档时的背包：空的，第一级的格数。读存档时现在配置的格数与在册物品也从它读。</param>
    /// <exception cref="SaveRejectedException">存档整份被拒：版本、分片、读不出来那几种。</exception>
    /// <exception cref="InvalidDataException">分片里哪一格读不出来，或者那片地对不上。</exception>
    public static WorldState LoadOrNew(
        string? text,
        SaveShardRegistry registry,
        IReadOnlyDictionary<string, CropDefinition> crops,
        PlotGrid sceneField,
        Bag newGameBag)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(crops);
        ArgumentNullException.ThrowIfNull(sceneField);
        ArgumentNullException.ThrowIfNull(newGameBag);

        if (text is null)
        {
            return WorldState.NewGame(sceneField.Clone(), newGameBag.Clone());
        }

        var world = WorldShards.Read(SaveFile.ReadFormalSave(registry, text), crops, newGameBag);
        var mismatch = LayoutMismatch(world.Plots, sceneField);
        return mismatch is null ? world : throw new InvalidDataException(mismatch);
    }

    /// <summary>一份世界写成正式存档的文本。</summary>
    public static string Write(SaveShardRegistry registry, WorldState world) =>
        SaveFile.WriteFormalSave(registry, WorldShards.Write(world));

    /// <summary>存档里那片地与场景里那片地哪里对不上；对得上时为 <c>null</c>。</summary>
    /// <remarks>改过那片地的位置或大小就会对不上。两边各列出对方没有的那几格，作者看得出是挪了还是改了大小。</remarks>
    public static string? LayoutMismatch(PlotGrid saved, PlotGrid scene)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(scene);

        var savedCells = saved.Cells.Select(c => c.Cell).ToList();
        var sceneCells = scene.Cells.Select(c => c.Cell).ToList();
        var onlyInScene = sceneCells.Except(savedCells).ToList();
        var onlyInSave = savedCells.Except(sceneCells).ToList();
        if (onlyInScene.Count == 0 && onlyInSave.Count == 0)
        {
            return null;
        }

        return $"存档里那片地（左上角 {saved.Origin}，{saved.Width}×{saved.Height}）与场景里那片地"
            + $"（左上角 {scene.Origin}，{scene.Width}×{scene.Height}）对不上："
            + $"场景里有而存档里没有 {Named(onlyInScene)}；存档里有而场景里没有 {Named(onlyInSave)}。"
            + "改回场景里那片地的位置与大小，或者删掉这份存档重开";
    }

    private static string Named(IReadOnlyList<GridCell> cells) => cells.Count switch
    {
        0 => "0 格",
        <= NamedCellLimit => $"{cells.Count} 格：{string.Join("、", cells)}",
        _ => $"{cells.Count} 格：{string.Join("、", cells.Take(NamedCellLimit))} 等",
    };
}
