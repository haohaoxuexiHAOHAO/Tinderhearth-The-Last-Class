using Godot;
using Godot.Collections;
using Tinderhearth.Rules.Economy;

namespace Tinderhearth.World.Farm;

/// <summary>一种作物那几张图。在 Godot 里存成 .tres，由作者配。</summary>
/// <remarks>
/// 同一种作物的信息分成两半，分界是「它进不进判定」。阶段天数、季节列表、收获后退回哪一阶段、
/// 产出哪种物品是行为，写在 <see cref="CropDefinition"/> 里（数据文件，规则层读）；长什么样在
/// 这里。所以每一格配没配只能从这里读出来，规则层看不见任何素材路径。
///
/// 张数不是固定的，等于这条作物自己声明的阶段数（<see cref="CropDefinition.StageCount"/>）外加
/// 一张变异成熟、一张枯死。速生菜交的图比慢熟的少，两边都不用改代码。校验在规则层的 <see cref="CropArtCheck"/>，
/// 全部作物素材一次核、一次全报；这里只交出每一格配没配（<see cref="ToEntry"/>）。
///
/// 变异成熟与枯死那两张不塞进 <see cref="StageTextures"/> 末尾：它们都不是生长阶段，一张是待收那一株
/// 变了的样子，一张是 <see cref="PlotState.Withered"/> 那个状态的样子。混进同一个数组之后要按下标挑，
/// 而挑错了不报错，表现只是作物显示成了别的那一张。
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

    /// <summary>长成那一晚掷中变异的那一株，待收时显示这一张，替成熟那一张。</summary>
    /// <remarks>
    /// 每种作物都可能变异，所以每种都要配。它与成熟那一张只靠颜色分也行：那一下答的是收上来是什么品质，
    /// 不是能不能收（设计仓 production/场景绘制约定.md 的「作物按它声明的阶段数交付」一节）。
    /// </remarks>
    [Export]
    public Texture2D? MutatedRipeTexture { get; set; }

    /// <summary>枯死之后停在地里的那一张。清掉枯株之前玩家一直看着它。</summary>
    [Export]
    public Texture2D? WitheredTexture { get; set; }

    /// <summary>这份素材配了什么：给哪一种作物、每一格配没配。交给 <see cref="CropArtCheck"/> 核。</summary>
    /// <remarks>
    /// 核在载入时一次判完，不等到显示那一刻。缺图在显示时的表现是那一格空着，而玩家分不清
    /// 「这一格没种东西」和「这一格种了但没图」。
    /// </remarks>
    public CropArtEntry ToEntry() => new(
        CropId,
        [.. StageTextures.Select(texture => texture is not null)],
        MutatedRipeTexture is not null,
        WitheredTexture is not null);

    /// <summary>某个阶段该显示哪一张。</summary>
    public Texture2D TextureForStage(int stage) => StageTextures[stage];
}
