namespace Tinderhearth.Rules.Economy;

/// <summary>一份作物素材里配了什么：给哪一种作物、生长阶段那几格各配没配、变异成熟与枯死那两格配没配。</summary>
/// <remarks>由引擎层从作物素材读出来。规则层只看每一格配没配，看不见任何图片路径。</remarks>
/// <param name="CropId">它配给哪一种作物。</param>
/// <param name="StagesFilled">生长阶段那一组，每一项配没配，顺序即生长顺序。</param>
/// <param name="HasMutatedRipe">变异成熟那一张配没配。</param>
/// <param name="HasWithered">枯死那一张配没配。</param>
public sealed record CropArtEntry(string CropId, IReadOnlyList<bool> StagesFilled, bool HasMutatedRipe, bool HasWithered);

/// <summary>农场载入时那一次核对：作物素材缺哪几张、配给了谁、哪几种作物没有素材，一次全报。</summary>
/// <remarks>
/// 一次全报而不是撞到第一处就停：一种作物一份素材，全部作物一起配的时候，一处一处改、一次一次重跑，作者要来回
/// 跑很多趟。张数按那条作物自己声明的阶段数算，理由在设计仓 production/场景绘制约定.md 的「作物按它声明的阶段数交付」一节。
///
/// 两份素材配给同一种作物单独报一条：配好一份再复制、忘了改标识，是照着指南配的时候最容易出的错，而不报的话
/// 后一份会顶掉前一份，表现只是另一种作物「有定义但没有素材」，绕一圈才找得到原因。
/// </remarks>
public static class CropArtCheck
{
    /// <summary>核全部作物素材。全对返回空表；顺序先按素材在那一组里的序号，最后是没有素材的那几种。</summary>
    /// <param name="crops">已载入的全部作物定义。</param>
    /// <param name="arts">那一组作物素材，顺序与检查器里一致；空着的那一项是 <c>null</c>。</param>
    public static IReadOnlyList<string> Problems(IEnumerable<CropDefinition> crops, IReadOnlyList<CropArtEntry?> arts)
    {
        ArgumentNullException.ThrowIfNull(crops);
        ArgumentNullException.ThrowIfNull(arts);

        var byId = crops.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var problems = new List<string>();
        var firstIndexOf = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < arts.Count; index++)
        {
            if (arts[index] is not { } art)
            {
                problems.Add($"Crop Arts 第 {index} 项（从 0 数）是空的");
                continue;
            }

            if (!firstIndexOf.TryAdd(art.CropId, index))
            {
                problems.Add($"Crop Arts 第 {firstIndexOf[art.CropId]} 项与第 {index} 项（从 0 数）都配给了作物「{art.CropId}」"
                    + " —— 复制出来的那一份多半没改 Crop Id");
            }

            if (!byId.TryGetValue(art.CropId, out var crop))
            {
                problems.Add($"素材配的作物「{art.CropId}」没有对应的作物定义 —— 标识要与作物定义里的 id 逐字符一致");
                continue;
            }

            problems.AddRange(ProblemsOf(art, crop));
        }

        var missing = byId.Keys.Where(id => !firstIndexOf.ContainsKey(id)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            problems.Add($"这几种作物有定义但没有素材：{string.Join("、", missing)} —— 每一种都要在 Crop Arts 里配一份");
        }

        return problems;
    }

    private static IEnumerable<string> ProblemsOf(CropArtEntry art, CropDefinition crop)
    {
        if (art.StagesFilled.Count != crop.StageCount)
        {
            yield return $"作物「{crop.Id}」声明了 {crop.StageCount} 个阶段（{crop.StageDays.Count} 个计时阶段加成熟），"
                + $"所以生长阶段那一组（Stage Textures）要有 {crop.StageCount} 张，实际 {art.StagesFilled.Count} 张";
        }

        for (var stage = 0; stage < art.StagesFilled.Count; stage++)
        {
            if (!art.StagesFilled[stage])
            {
                yield return $"作物「{crop.Id}」的第 {stage} 阶段那一张是空的（阶段从 0 起算，第 {crop.RipeStage} 阶段是成熟）";
            }
        }

        if (!art.HasMutatedRipe)
        {
            yield return $"作物「{crop.Id}」缺变异成熟那一张（Mutated Ripe Texture）—— 每种作物长成时都可能变异，"
                + "变了的那一株待收时显示它";
        }

        if (!art.HasWithered)
        {
            yield return $"作物「{crop.Id}」缺枯死那一张（Withered Texture）—— 枯株会停在地里等玩家清掉，所以那个状态一定会被看到";
        }
    }
}
