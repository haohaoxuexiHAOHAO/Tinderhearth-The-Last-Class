using Tinderhearth.Rules.Foundation.Actors;

namespace Tinderhearth.Rules.UI;

/// <summary>
/// 世界那一层往一个目标身上挂东西时用的那两样，核过的：头顶离脚底多高、哪一档体型。
/// </summary>
/// <remarks>
/// 只能经 <see cref="Of"/> 造，那里核：头顶高度要大于 0，体型档不许是「没报」。没报的目标挂的那一刻当场抛，
/// 并说清是哪个目标缺了哪一项 —— 不报的话血条会悄悄落进身体里，而那不报错。
/// </remarks>
public sealed record WorldUIAnchor
{
    private WorldUIAnchor(int headHeightWorldPx, BodyTier tier)
    {
        HeadHeightWorldPx = headHeightWorldPx;
        Tier = tier;
    }

    /// <summary>头顶离脚底多高，世界像素，从脚底往上量。</summary>
    public int HeadHeightWorldPx { get; }

    /// <summary>体型档。</summary>
    public BodyTier Tier { get; }

    /// <summary>核一个目标报上来的两样，都报了就造一个；缺哪一项都抛，一次说全。</summary>
    /// <param name="target">报错时说给作者听的目标名字。</param>
    /// <param name="headHeightWorldPx">它报的头顶高度。</param>
    /// <param name="tier">它报的体型档。</param>
    public static WorldUIAnchor Of(string target, int headHeightWorldPx, BodyTier tier)
    {
        var missing = new List<string>();
        if (headHeightWorldPx <= 0)
        {
            missing.Add($"头顶高度（报的是 {headHeightWorldPx}，要从脚底往上量、大于 0）");
        }

        if (!Enum.IsDefined(tier) || tier == BodyTier.Unset)
        {
            missing.Add($"体型档（报的是 {tier}）");
        }

        return missing.Count == 0
            ? new WorldUIAnchor(headHeightWorldPx, tier)
            : throw new ArgumentException(
                $"目标「{target}」没报{string.Join("、", missing)} —— 世界那一层的血条与伤害数字按这两样摆");
    }
}
