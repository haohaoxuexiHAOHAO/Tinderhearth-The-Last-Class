using System.Security.Cryptography;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>
/// 画界面素材之前定下的那几样连起来走一遍：两档尺寸 → 按场合放的 HUD → 关卡那一套不进纵深带 → 界面色对得上色板
/// → 世界那一层按目标报的头顶摆。
/// </summary>
/// <remarks>
/// 每一段各有单测（<see cref="UISkeletonTests"/>、<see cref="HudTests"/>、<see cref="PaletteTests"/>、
/// <see cref="WorldSpaceTests"/>、<see cref="UIArtTests"/>）。这里判的是段与段之间的交接：下一段用的数就是上一段推出来的
/// 那一个，也就是作者画好的那张图的尺寸。一段改了、下一段没跟，各自的单测照样通过，这里会红。
///
/// 失败路径各拿一处坏数据撞一次，看判据点不点得出是哪一块、哪一档、哪个目标、哪一格。坏数据都在内存里造，读色板只读不写，
/// 「独立可重复」那一段判这个。只测到规则层：节点摆得对不对、画面上看不看得清，由作者实机看。
/// </remarks>
public class UIArtBaselineE2ETests
{
    /// <summary>实测过的那个宽窗口的逻辑宽度（3840×2130 的窗口得到 649×360）。</summary>
    private const int StretchedWidth = 649;

    private static readonly int[] Widths = [UIMetrics.BaseWidth, StretchedWidth];

    // ── 主路径 ──────────────────────────────────────────────────────

    [Fact]
    public void 主路径_两档尺寸到HUD放置到纵深带到色板到世界那一层一路接得上()
    {
        // 一、两档尺寸由既有单位推出；作者画好的格子与资源条标签正是这么大。
        Assert.Equal(UIMetrics.IconSmall + (2 * (UIMetrics.CellOutline + UIMetrics.CellPadding)), UIMetrics.Cell);
        Assert.Equal(UIMetrics.Grid, UIMetrics.Mini);
        Assert.Equal(new PixelSize(UIMetrics.Cell, UIMetrics.Cell), UIArtManifest.Find("kit", "slot_normal").Size);
        Assert.Equal(new PixelSize(UIMetrics.Mini, UIMetrics.Mini), UIArtManifest.Find("hud", "gauge_icon_hp").Size);

        // 二、HUD 的块由这两档排出来：那条栏是两头框边加一排格子与格距，栏高就是画好的那条栏的高，两种场合是同一条；
        //     资源条前面的标签是迷你那一档。
        var cells = HudLayout.ActionBarCellCount;
        var bar = HudLayout.SizeOf(HudBlock.ActionBar, UIContext.Level);
        Assert.Equal(
            (HudLayout.ActionBarCapX * 2) + (cells * UIMetrics.Cell) + ((cells - 1) * HudLayout.ActionBarCellGap),
            bar.Width);
        Assert.Equal(UIArtManifest.Find("hud", "actionbar_frame").Size.Height, bar.Height);
        Assert.Equal(bar, HudLayout.SizeOf(HudBlock.ActionBar, UIContext.Base));
        Assert.Equal(UIMetrics.Mini, HudLayout.GaugeLabelSize);

        // 三、两种场合各放各的：关卡全贴顶边，基地贴底边的两个角。
        Assert.All(HudLayout.BlocksIn(UIContext.Level), block => Assert.True(
            HudLayout.PlacementOf(block, UIContext.Level).Anchor is HudAnchor.TopLeft or HudAnchor.TopRight,
            $"关卡里的 {block} 没贴顶边"));
        Assert.Equal(HudAnchor.BottomLeft, HudLayout.PlacementOf(HudBlock.Resources, UIContext.Base).Anchor);
        Assert.Equal(HudAnchor.BottomRight, HudLayout.PlacementOf(HudBlock.ActionBar, UIContext.Base).Anchor);

        // 四、640 宽与撑宽之后：关卡那一套每一块都在顶上那一段里，基地那一套不压相机死区那块可读区，两条栏横坐标对齐。
        foreach (var width in Widths)
        {
            Assert.Empty(HudLayout.BlocksBelowTopBand(width, UIMetrics.BaseHeight));
            Assert.Empty(HudLayout.BlocksOverActorBand(width, UIMetrics.BaseHeight));
            Assert.Equal(
                HudLayout.RectOf(HudBlock.ActionBar, UIContext.Level, width, UIMetrics.BaseHeight).X,
                HudLayout.RectOf(HudBlock.ActionBar, UIContext.Base, width, UIMetrics.BaseHeight).X);
        }

        // 五、代码设的界面色每一个都带着色板里的项名，值与那份色板对得上。
        var named = PaletteTests.NamedEntries();
        Assert.NotEmpty(named);    // 一项都没找到时下一句会空跑通过
        var problems = PaletteFile.Mismatches(named, PaletteFile.Read());
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.Empty(PaletteTests.BareColors());

        // 六、世界那一层按目标报的头顶摆：两个只差身高的目标，偏移正好差那么多。精英血条就是 HUD 那根条 ——
        //     一样高（画好的那张槽的高），大体型那一档一样长。
        const int humanHead = 48;
        const int largeHead = 80;
        var human = WorldUIAnchor.Of("人形精英", humanHead, BodyTier.Human);
        var large = WorldUIAnchor.Of("大体型精英", largeHead, BodyTier.Large);
        Assert.Equal(-(humanHead + UIMetrics.ItemGap + WorldUILayout.EliteBarHeight), WorldUILayout.EliteBarTopY(human));
        Assert.Equal(largeHead - humanHead, WorldUILayout.EliteBarTopY(human) - WorldUILayout.EliteBarTopY(large));
        Assert.Equal(
            largeHead - humanHead,
            WorldUILayout.DamageStartOffsetY(human) - WorldUILayout.DamageStartOffsetY(large));
        Assert.Equal(HudLayout.GaugeBarHeight, WorldUILayout.EliteBarHeight);
        Assert.Equal(WorldUILayout.EliteBarHeight, UIArtManifest.Find("hud", "bar_track").Size.Height);
        Assert.Equal(HudLayout.GaugeBarWidth, WorldUILayout.EliteBarWidthOf(BodyTier.Large));
    }

    // ── 失败路径 ────────────────────────────────────────────────────

    [Theory]
    [InlineData(UIMetrics.BaseWidth)]
    [InlineData(StretchedWidth)]
    public void 失败路径_关卡任一块挪进纵深带一像素就点名是那一块(int width)
    {
        // 主路径里判纵深带的那一句（BlocksBelowTopBand）拿每一块去过 ExceedsTopBand，而它只收自己算的那一套，
        // 所以这里照它的做法过一遍挪过一块的那一套。要判得出越界，而且报的正是挪下去的那一块、不连累别的块；
        // 底边正好贴着那一段的下沿不算越界。
        var rects = HudLayout.RectsOf(UIContext.Level, width, UIMetrics.BaseHeight);
        Assert.True(HudLayout.BlocksBelowTopBand(width, UIMetrics.BaseHeight).Count == 0, "挪之前就有块越界了 —— 先看主路径那一条");
        foreach (var (block, _) in rects)
        {
            Assert.Empty(CaughtAfterMoving(rects, block, HudLayout.LevelTopBandHeight));
            Assert.Equal(new[] { block }, CaughtAfterMoving(rects, block, HudLayout.LevelTopBandHeight + 1));
        }
    }

    [Fact]
    public void 失败路径_色板里被界面引用的任一档改了值或被删掉都点名是谁取的哪一档()
    {
        // 色板与代码各抄一份值，一边改了另一边没跟不报编译错。被引用的每一档各撞一次，坏的那份色板只在内存里。
        var named = PaletteTests.NamedEntries();
        var palette = PaletteFile.Read();
        foreach (var name in named.Select(e => e.Entry.Name).Distinct())
        {
            Assert.True(palette.TryGetValue(name, out var old), $"「{name}」本来就不在色板里");
            var owners = named.Where(e => e.Entry.Name == name).Select(e => e.Owner).ToList();

            var changed = new Dictionary<string, PixelColor>(palette) { [name] = old with { R = (byte)(old.R ^ 1) } };
            var removed = new Dictionary<string, PixelColor>(palette);
            removed.Remove(name);

            foreach (var (bad, why) in new[] { (changed, "色板里是"), (removed, "不在色板里") })
            {
                var problems = PaletteFile.Mismatches(named, bad);
                Assert.Equal(owners.Count, problems.Count);
                Assert.All(owners, owner => Assert.Contains(problems, p =>
                    p.StartsWith($"{owner} 取的「{name}」", StringComparison.Ordinal) && p.Contains(why, StringComparison.Ordinal)));
            }
        }
    }

    [Fact]
    public void 失败路径_目标没报头顶高度或体型档就挂不上而且点名是谁缺了哪一项()
    {
        var noHead = Assert.Throws<ArgumentException>(() => WorldUIAnchor.Of("木桩甲", 0, BodyTier.Human));
        Assert.Contains("「木桩甲」", noHead.Message);
        Assert.Contains("头顶高度", noHead.Message);

        var noTier = Assert.Throws<ArgumentException>(() => WorldUIAnchor.Of("木桩甲", 48, BodyTier.Unset));
        Assert.Contains("「木桩甲」", noTier.Message);
        Assert.Contains("体型档", noTier.Message);

        // 主路径里世界那一层的那几个偏移只收核过的这一份，而它只有上面那一个入口：没有公开的构造函数，两项也都改不了。
        // 有人加一个公开构造函数或 init，没报高度的目标就绕得过去，而那不报错。
        Assert.Empty(typeof(WorldUIAnchor).GetConstructors());
        Assert.All(typeof(WorldUIAnchor).GetProperties(), p => Assert.False(p.CanWrite, $"{p.Name} 改得动"));
    }

    [Fact]
    public void 失败路径_素材资源清空几格启动时就一次列出几格而且每格带名字尺寸与状态()
    {
        // 照清单填好的一份里清空两格（作者实机删格子那一步的规则层那一半）：报两条，各点名一格；全清空就一格一条。
        var filled = UIArtTests.Filled(UIArtManifest.Pieces);
        UIArtPiece[] cleared = [UIArtManifest.Find("hud", "objective"), UIArtManifest.Find("kit", "slot_normal")];
        var entries = filled
            .Select(e => cleared.Any(p => p.Group == e.Group && p.Name == e.Name) ? e with { Size = null, Nine = null } : e)
            .ToList();

        var problems = UIArtCheck.Problems(entries);

        Assert.Equal(cleared.Length, problems.Count);
        Assert.All(cleared, piece => Assert.Contains(problems, p => p.Contains(piece.Describe(), StringComparison.Ordinal)));
        Assert.Equal(
            UIArtManifest.Pieces.Count,
            UIArtCheck.Problems([.. filled.Select(e => e with { Size = null, Nine = null })]).Count);
    }

    // ── 独立可重复 ──────────────────────────────────────────────────

    [Fact]
    public void 独立性_读色板只读不写而且内存里改的那一档不留到下一次读()
    {
        var file = Path.Combine(RepoFiles.Root(), "assets", "self-drawn", "palette.gpl");
        var hash = SHA256.HashData(File.ReadAllBytes(file));
        var written = File.GetLastWriteTimeUtc(file);

        // 照上面那几条的做法：读一次，在内存里改一档，拿改过的那份判一次，再读一次。
        var first = PaletteFile.Read();
        var ink = first[HudPalette.Ink.Name];
        var edited = new Dictionary<string, PixelColor>(first) { [HudPalette.Ink.Name] = ink with { G = (byte)(ink.G ^ 1) } };
        Assert.NotEmpty(PaletteFile.Mismatches(PaletteTests.NamedEntries(), edited));
        var second = PaletteFile.Read();

        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(file)));
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
        Assert.Equal(
            first.OrderBy(e => e.Key, StringComparer.Ordinal),
            second.OrderBy(e => e.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void 独立性_几条测试共用的那份素材清单改不动()
    {
        // 清单是静态的、所有测试共用。改得动的话，一条测试改过的清单会留给下一条，结果就取决于谁先跑。
        Assert.False(UIArtManifest.Pieces is IList<UIArtPiece> { IsReadOnly: false });
    }

    /// <summary>把关卡那一套里的一块挪到底边落在 <paramref name="bottom"/>，其余不动，返回判出越过顶上那一段的那几块。</summary>
    private static List<HudBlock> CaughtAfterMoving(
        IReadOnlyList<(HudBlock Block, HudRect Rect)> rects, HudBlock moved, int bottom) =>
        [.. rects
            .Select(r => r.Block == moved ? (r.Block, Rect: r.Rect with { Y = bottom - r.Rect.Height }) : r)
            .Where(r => HudLayout.ExceedsTopBand(r.Rect))
            .Select(r => r.Block)];
}
