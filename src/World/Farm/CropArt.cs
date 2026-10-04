using Godot;
using Godot.Collections;
using Tinderhearth.Rules.Economy;

namespace Tinderhearth.World.Farm;

/// <summary>一种作物那几张图。在 Godot 里存成 .tres，由作者配。</summary>
/// <remarks>
/// 同一种作物的信息分成两半，分界是「它进不进判定」。阶段天数、季节列表、收获后退回哪一阶段、
/// 产出哪种物品是行为，写在 <see cref="CropDefinition"/> 里（数据文件，规则层读）；长什么样在
/// 这里。所以「缺哪张图」只能在这里报错，规则层看不见任何素材路径。
///
/// 张数不是固定的，等于这条作物自己声明的阶段数（<see cref="CropDefinition.StageCount"/>）外加
/// 一张枯死。速生菜交的图比慢熟的少，两边都不用改代码。校验在 <see cref="RequireMatching"/>。
///
/// 枯死那张不塞进 <see cref="StageTextures"/> 末尾，因为它不是一个生长阶段，而是
/// <see cref="PlotState.Withered"/> 那个状态的样子。混进同一个数组之后，读的时候要把下标减一，
/// 而减错了不报错，表现只是作物显示成上一阶段的图。
/// </remarks>
[GlobalClass]
public partial class CropArt : Resource
{
    /// <summary>它配的是哪一种作物。要和那条作物定义里的标识逐字符一致。</summary>
    /// <remarks>
    /// 靠标识配对，不靠在检查器里排顺序。顺序对不上不报错，只表现为「萝卜长出了土豆的样子」。
    /// </remarks>
    [Export]
    public string CropId { get; set; } = "";

    /// <summary>每个生长阶段各一张，顺序就是生长顺序，最后一张是成熟。</summary>
    /// <remarks>
    /// 成熟那一张要靠轮廓和前一张分开，不能只靠换颜色。因为「这一格能不能收」在画面上只有这一处
    /// 答案，刻意不做悬在格子上方的状态图标。这一条机器判不了，要作者实机看。
    /// </remarks>
    [Export]
    public Array<Texture2D> StageTextures { get; set; } = [];

    /// <summary>枯死之后停在地里的那一张。清掉枯株之前玩家一直看着它。</summary>
    [Export]
    public Texture2D? WitheredTexture { get; set; }

    /// <summary>这份素材配不配得上那条作物定义。配不上就抛，并说清缺的是哪一张。</summary>
    /// <remarks>
    /// 在载入时一次判完，不等到显示那一刻。缺图在显示时的表现是那一格空着，而玩家分不清
    /// 「这一格没种东西」和「这一格种了但没图」。
    /// </remarks>
    public void RequireMatching(CropDefinition crop)
    {
        ArgumentNullException.ThrowIfNull(crop);

        if (CropId != crop.Id)
        {
            throw new InvalidOperationException(
                $"这份 {nameof(CropArt)} 的 {nameof(CropId)} 是「{CropId}」，"
                    + $"而要配的作物定义是「{crop.Id}」，两边的标识必须逐字符一致");
        }

        if (StageTextures.Count != crop.StageCount)
        {
            throw new InvalidOperationException(
                $"作物「{crop.Id}」声明了 {crop.StageCount} 个阶段"
                    + $"（{crop.StageDays.Count} 个计时阶段加成熟），"
                    + $"所以 {nameof(StageTextures)} 要有 {crop.StageCount} 张，实际 {StageTextures.Count} 张");
        }

        for (var stage = 0; stage < StageTextures.Count; stage++)
        {
            if (StageTextures[stage] is null)
            {
                throw new InvalidOperationException(
                    $"作物「{crop.Id}」的第 {stage} 阶段那一张是空的"
                        + $"（阶段从 0 起算，第 {crop.RipeStage} 阶段是成熟）");
            }
        }

        if (WitheredTexture is null)
        {
            throw new InvalidOperationException(
                $"作物「{crop.Id}」缺 {nameof(WitheredTexture)}。枯株会停在地里等玩家清掉，"
                    + "所以那个状态一定会被看到");
        }
    }

    /// <summary>某个阶段该显示哪一张。</summary>
    public Texture2D TextureForStage(int stage) => StageTextures[stage];
}
