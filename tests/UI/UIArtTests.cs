using System.Buffers.Binary;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>
/// 素材资源那份清单：与磁盘上的图对得上、能由排版单位推出的那几档对得上单位、载入时的核对一次报全。
/// </summary>
/// <remarks>
/// 清单（<see cref="UIArtManifest"/>）由代码仓 <c>tools/ui_art_resource.py</c> 照生成器清单写出，所以它与磁盘上
/// 那批图对不上，说明有人改了生成器或换了图却没重跑那个入口 —— 前两条判的就是这个。读图只读 PNG 文件头里的
/// 宽高，不解码像素。
///
/// 第三条把「这一件属于哪一档」按名字写死在这里：按尺寸反推档位会自证循环（改了单位之后清单一重写，
/// 那一件就悄悄不属于任何一档了）。
/// </remarks>
public class UIArtTests
{
    private static readonly string UIDirectory = Path.Combine(RepoFiles.Root(), "assets", "self-drawn", "ui");

    [Fact]
    public void 清单里每一件在磁盘上都有而且一样大()
    {
        var problems = new List<string>();
        foreach (var piece in UIArtManifest.Pieces)
        {
            var file = Path.Combine(UIDirectory, piece.Group, $"{piece.Name}.png");
            if (!File.Exists(file))
            {
                problems.Add($"{piece.FilePath} 不在磁盘上");
            }
            else if (PngSize(file) is var size && size != piece.Size)
            {
                problems.Add($"{piece.FilePath}：磁盘上是 {size}，清单里是 {piece.Size}");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void 磁盘上每一张界面件都在清单里()
    {
        // 作者直接画的那几张（主菜单的标题与底图）不进素材资源，单列在清单里，这里放过。
        var listed = UIArtManifest.Pieces.Select(p => p.FilePath).Concat(UIArtManifest.AuthorOwned).ToHashSet();
        var extra = Directory.EnumerateFiles(UIDirectory, "*.png", SearchOption.AllDirectories)
            .Select(f => "ui/" + Path.GetRelativePath(UIDirectory, f).Replace('\\', '/'))
            .Where(rel => !listed.Contains(rel))
            .ToList();

        Assert.Empty(extra);
    }

    /// <summary>
    /// 界面绘制约定「尺寸阶梯」那张表里由排版单位定的几档，加上 HUD 排版照着它们的尺寸排的那几件。
    /// 改了单位，这几件就要重画；作者换了图，HUD 的排版就要跟着改。哪一边先动，这条先红。
    /// </summary>
    [Fact]
    public void 能由排版单位推出的那几档都对得上单位()
    {
        (string[] Patterns, int? Width, int? Height, string Tier)[] tiers =
        [
            (["kit/slot_normal", "kit/slot_disabled", "kit/slot_locked", "kit/slot_focus", "kit/rarity_edge",
              "kit/socket_*"], UIMetrics.Cell, UIMetrics.Cell, "格子"),
            (["kit/slot_select"], UIMetrics.Cell + (UIMetrics.SelectOutset * 2),
                UIMetrics.Cell + (UIMetrics.SelectOutset * 2), "选中框"),
            (["kit/mini_*", "kit/heart_*", "kit/star_*", "kit/pip_enhance_*", "hud/gauge_icon_*", "panel/map_*"],
                UIMetrics.Mini, UIMetrics.Mini, "迷你"),
            (["tab/*", "kit/iconbutton_*", "kit/button_icon_*", "kit/check_*", "kit/cursor_*", "glyph/mouse_*",
              "glyph/key_digit_*", "hud/objective", "hud/screen_clear", "hud/down_mark", "world/hint_icon",
              "world/immune", "world/device_*", "world/place_*", "world/drop_base", "panel/attr_*", "panel/node_*",
              "panel/codex_unknown", "panel/coin"], UIMetrics.IconSmall, UIMetrics.IconSmall, "小图标"),
            (["panel/result_*"], UIMetrics.IconLarge, UIMetrics.IconLarge, "大图标"),
            (["hud/portrait_frame"], HudLayout.PortraitSize, HudLayout.PortraitSize, "头像框"),
            (["kit/row_*", "glyph/keycap"], null, UIMetrics.LineHeight, "行"),
            (["hud/bar_track"], null, HudLayout.GaugeBarHeight, "资源条的槽"),
            (["hud/bar_fill_*"], null, HudLayout.GaugeFillHeight, "资源条的填充"),
            (["hud/vigor_tube_*"], HudLayout.VigorTubeWidth, null, "精力条"),
            (["hud/actionbar_frame"], null, HudLayout.SizeOf(HudBlock.ActionBar, UIContext.Level).Height, "那条栏"),
            (["world/ring_cast", "world/ring_cast_fill"], WorldUILayout.RingWidth, WorldUILayout.RingHeight, "读条圆环"),
            (["world/cell_usable", "world/cell_blocked"], WorldUILayout.CellCursorSide(UIMetrics.BaseUnit),
                WorldUILayout.CellCursorSide(UIMetrics.BaseUnit), "当前操作格"),
        ];

        var problems = new List<string>();
        foreach (var (patterns, width, height, tier) in tiers)
        {
            foreach (var pattern in patterns)
            {
                var members = UIArtManifest.Pieces.Where(p => Matches(p, pattern)).ToList();
                if (members.Count == 0)
                {
                    problems.Add($"{tier}那一档的「{pattern}」一件都没对上 —— 改了名字就跟着改这张表");
                }

                problems.AddRange(members
                    .Where(p => (width is { } w && p.Size.Width != w) || (height is { } h && p.Size.Height != h))
                    .Select(p => $"{p.FilePath} 是 {p.Size}，{tier}那一档要宽 {width?.ToString() ?? "不限"}、"
                        + $"高 {height?.ToString() ?? "不限"}"));
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void 九宫格件的画布比两头的边距加起来大()
    {
        var bad = UIArtManifest.Pieces
            .Where(p => p.Nine is { } n && (n.Left + n.Right >= p.Size.Width || n.Top + n.Bottom >= p.Size.Height))
            .Select(p => p.FilePath)
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void 每个顶层页签都有一张图标和它的置灰版()
    {
        // 页签图标的文件名就是页签的标识；手环加了页签而模板里没画，这条点名。
        var names = UIArtManifest.Pieces.Where(p => p.Group == "tab").Select(p => p.Name).ToHashSet();
        var missing = Wristband.Tabs
            .SelectMany(t => new[] { t.Id, $"{t.Id}_disabled" })
            .Where(n => !names.Contains(n))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void 清单里同一个目录下没有重名的格子()
    {
        var keys = UIArtManifest.Pieces.Select(p => p.FilePath).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void 一份空的素材资源一次列出全部缺项而且每一项带名字尺寸与状态()
    {
        var empty = UIArtManifest.Pieces.Select(p => new UIArtEntry(p.Group, p.Name, null, null)).ToList();

        var problems = UIArtCheck.Problems(empty);

        Assert.Equal(UIArtManifest.Pieces.Count, problems.Count);
        Assert.All(UIArtManifest.Pieces.Zip(problems), pair =>
        {
            Assert.Contains(pair.First.FilePath, pair.Second);
            Assert.Contains(pair.First.Size.ToString(), pair.Second);
            Assert.Contains(pair.First.State, pair.Second);
        });
        Assert.Contains(problems, p => p.Contains("kit/button_pressed.png", StringComparison.Ordinal)
            && p.Contains("pressed 那一态", StringComparison.Ordinal));
    }

    [Fact]
    public void 照清单填好的一份一条都不报()
    {
        Assert.Empty(UIArtCheck.Problems(Filled(UIArtManifest.Pieces)));
    }

    [Fact]
    public void 换上去的图尺寸不对就点名是哪一格()
    {
        var manifest = new[] { Plain("slot_normal", 22, 22), Plain("slot_select", 26, 26) };
        var entries = Filled(manifest).ToList();
        entries[0] = entries[0] with { Size = new PixelSize(16, 16) };

        var problem = Assert.Single(UIArtCheck.Problems(manifest, entries));

        Assert.Contains("ui/kit/slot_normal.png 是 16×16，应当是 22×22", problem);
    }

    [Theory]
    [InlineData(6, 0, 6, 0, "横着")]
    [InlineData(4, 6, 4, 6, "竖着")]
    public void 九宫格边距撑破画布就点名(int left, int top, int right, int bottom, string direction)
    {
        var manifest = new[] { Plain("frame_thin_dark", 12, 12) with { Nine = new NineMargins(4, 4, 4, 4) } };
        var entries = new[] { new UIArtEntry("kit", "frame_thin_dark", new PixelSize(12, 12),
            new NineMargins(left, top, right, bottom)) };

        var problem = Assert.Single(UIArtCheck.Problems(manifest, entries));

        Assert.Contains("ui/kit/frame_thin_dark.png", problem);
        Assert.Contains(direction, problem);
    }

    [Fact]
    public void 资源比清单少一格或多一格都报()
    {
        var manifest = new[] { Plain("slot_normal", 22, 22), Plain("slot_focus", 22, 22) };
        var entries = new[]
        {
            new UIArtEntry("kit", "slot_normal", new PixelSize(22, 22), null),
            new UIArtEntry("kit", "slot_gone", new PixelSize(22, 22), null),
        };

        var problems = UIArtCheck.Problems(manifest, entries);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("没有 ui/kit/slot_focus.png", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("多了 ui/kit/slot_gone.png", StringComparison.Ordinal));
    }

    private static UIArtPiece Plain(string name, int width, int height) =>
        new("kit", name, new PixelSize(width, height), null, "", "测试用");

    private static List<UIArtEntry> Filled(IEnumerable<UIArtPiece> manifest) =>
        [.. manifest.Select(p => new UIArtEntry(p.Group, p.Name, p.Size, p.Nine))];

    private static bool Matches(UIArtPiece piece, string pattern) =>
        pattern.EndsWith('*')
            ? $"{piece.Group}/{piece.Name}".StartsWith(pattern[..^1], StringComparison.Ordinal)
            : $"{piece.Group}/{piece.Name}" == pattern;

    /// <summary>PNG 文件头里的宽高：8 字节签名之后第一块必是 IHDR，宽高各 4 字节、大端。</summary>
    private static PixelSize PngSize(string file)
    {
        Span<byte> head = stackalloc byte[24];
        using var stream = File.OpenRead(file);
        stream.ReadExactly(head);
        if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R')
        {
            throw new InvalidDataException($"{file} 不是 PNG，或者第一块不是 IHDR");
        }

        return new PixelSize(
            BinaryPrimitives.ReadInt32BigEndian(head[16..20]),
            BinaryPrimitives.ReadInt32BigEndian(head[20..24]));
    }
}
