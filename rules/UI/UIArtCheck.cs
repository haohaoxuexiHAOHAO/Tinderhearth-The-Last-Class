namespace Tinderhearth.Rules.UI;

/// <summary>
/// 素材资源载入时那一次核对：缺哪几格、哪张尺寸不对、哪件九宫格的边距撑破了画布，一次全报。
/// </summary>
/// <remarks>
/// 一次全报而不是撞到第一处就停：素材资源一百多格，一处一处改、一次一次重跑，作者要来回跑很多趟。
///
/// 尺寸按清单那一格核，不按「看起来差不多」：作者换上去的那张要保持原尺寸（设计仓 production/界面绘制约定.md
/// 「作者可以直接美化生成物」那一条），尺寸一变，九宫格边距、格子里图标放在哪、条的填充放在槽的哪一点都跟着错，
/// 而那几样都不报错，只表现为画面上歪了一两个像素。
///
/// 九宫格边距不跟清单比：作者重画边框之后在检查器里改边距，正是这一格存在的理由。这里只判它还成不成立 ——
/// 两头的边距加起来要比画布窄，否则中间没有能拉开的那一段。
/// </remarks>
public static class UIArtCheck
{
    /// <summary>照 <see cref="UIArtManifest"/> 核一份素材资源。全对返回空表。</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<UIArtEntry> entries) =>
        Problems(UIArtManifest.Pieces, entries);

    /// <summary>照给定的清单核一份素材资源。全对返回空表，顺序与清单一致。</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<UIArtPiece> manifest, IReadOnlyList<UIArtEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(entries);

        var problems = new List<string>();
        var filled = new Dictionary<(string, string), UIArtEntry>();
        foreach (var entry in entries)
        {
            if (!filled.TryAdd((entry.Group, entry.Name), entry))
            {
                problems.Add($"素材资源里 ui/{entry.Group}/{entry.Name}.png 那一格出现了两次");
            }
        }

        var listed = new HashSet<(string, string)>();
        foreach (var piece in manifest)
        {
            listed.Add((piece.Group, piece.Name));
            if (!filled.TryGetValue((piece.Group, piece.Name), out var entry))
            {
                problems.Add($"素材资源里没有 {piece.FilePath} 那一格 —— 资源类型没跟上生成器清单，"
                    + "在代码仓根目录重跑 python tools/ui_art_resource.py");
                continue;
            }

            if (entry.Size is not { } size)
            {
                problems.Add($"缺 {piece.Describe()}");
                continue;
            }

            if (size != piece.Size)
            {
                problems.Add($"{piece.FilePath} 是 {size}，应当是 {piece.Size} —— 换上去的图要保持原尺寸"
                    + $"（{piece.Use}）");
            }

            if (piece.Nine is not null)
            {
                problems.AddRange(NineProblems(piece, size, entry.Nine));
            }
        }

        foreach (var entry in entries.Where(e => !listed.Contains((e.Group, e.Name))))
        {
            problems.Add($"素材资源里多了 ui/{entry.Group}/{entry.Name}.png 那一格，生成器清单里没有它 —— "
                + "在代码仓根目录重跑 python tools/ui_art_resource.py");
        }

        return problems;
    }

    private static IEnumerable<string> NineProblems(UIArtPiece piece, PixelSize size, NineMargins? nine)
    {
        if (nine is not { } m)
        {
            yield return $"{piece.FilePath} 是九宫格件，那一格却没有边距";
            yield break;
        }

        if (m.Left < 0 || m.Top < 0 || m.Right < 0 || m.Bottom < 0)
        {
            yield return $"{piece.FilePath} 的九宫格边距（{m}）有负数";
        }

        if (m.Left + m.Right >= size.Width)
        {
            yield return $"{piece.FilePath} 的九宫格边距左 {m.Left} 加右 {m.Right} 不比宽 {size.Width} 窄，"
                + "中间没有能横着拉开的那一段";
        }

        if (m.Top + m.Bottom >= size.Height)
        {
            yield return $"{piece.FilePath} 的九宫格边距上 {m.Top} 加下 {m.Bottom} 不比高 {size.Height} 矮，"
                + "中间没有能竖着拉开的那一段";
        }
    }
}
