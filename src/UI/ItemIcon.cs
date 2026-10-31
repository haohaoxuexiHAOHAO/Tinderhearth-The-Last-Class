using Godot;
using Tinderhearth.Rules.Economy;

namespace Tinderhearth.UI;

/// <summary>一个物品的图标。在 Godot 里存成 .tres，由作者配，一个物品一份。</summary>
/// <remarks>
/// 资源上写它是哪个物品，物品定义上没有这一格（设计仓 design/物品系统.md 字段表的「图标」与「变异时的图标」两行），
/// 与作物素材同一个做法：长什么样归作者在 Godot 里配，规则层只看每一格配没配（<see cref="ToEntry"/>），
/// 缺不缺、重不重、变异那一格写没写，由 <see cref="ItemIconCheck.Problems"/> 在载入时一次全报。
///
/// 变异那一张另开一格、不塞进同一个数组：它不是「第几张」，而是「品质是变异的那一堆换用哪一张」，混进数组就要按下标挑，
/// 挑错了不报错。不另画时勾「同原图」，而不是把图标那一张再拖一遍：拖一遍与忘了改看起来一模一样。
/// </remarks>
[GlobalClass]
public partial class ItemIcon : Resource
{
    /// <summary>它配的是哪一个物品。要和那份物品定义里的标识逐字符一致。</summary>
    /// <remarks>靠标识配对，不靠在检查器里排顺序：顺序对不上不报错，只表现为种子画成了别的种子。</remarks>
    [Export]
    public string ItemId { get; set; } = "";

    /// <summary>图标那一张。无品质的东西、以及品质不是变异的那几档都画它。</summary>
    [Export]
    public Texture2D? Icon { get; set; }

    /// <summary>变异那一张：品质是变异的那一堆画它。只有声明了有品质的物品才配；不另画就勾 <see cref="MutatedSameAsIcon"/>。</summary>
    [Export]
    public Texture2D? MutatedIcon { get; set; }

    /// <summary>变异那一张同原图：显式写出「不另画」，与「忘了配」分得开。</summary>
    [Export]
    public bool MutatedSameAsIcon { get; set; }

    /// <summary>这份图标配了什么：给哪一个物品、每一格配没配。交给 <see cref="ItemIconCheck"/> 核。</summary>
    public ItemIconEntry ToEntry() => new(ItemId, Icon is not null, MutatedIcon is not null, MutatedSameAsIcon);

    /// <summary>这一堆画哪一张。只在载入时核过之后用，所以这里不再判缺。</summary>
    /// <param name="qualityId">那一堆的品质；无品质的东西是 <c>null</c>。</param>
    public Texture2D TextureFor(string? qualityId) =>
        ItemIconCheck.ShowsMutated(qualityId) && !MutatedSameAsIcon ? MutatedIcon! : Icon!;
}
