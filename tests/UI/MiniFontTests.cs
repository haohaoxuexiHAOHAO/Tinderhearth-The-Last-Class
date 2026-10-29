using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>迷你字：取格与字距照素材清单那一件、挨着的两个字只隔一条描边、键盘那几个键名写得出来。</summary>
/// <remarks>
/// 画出来清不清楚由作者实机看；这里判的是两样看不出来的事：字距多挪一列只表现为数字之间宽了一点，
/// 改了绑定而那个键名不在字形表里，要等到换上那个键的那一刻才抛。
/// </remarks>
public class MiniFontTests
{
    [Fact]
    public void 一格多大照素材清单那一排等分()
    {
        Assert.Equal(MiniFont.Sheet.Size.Width, MiniFont.CellWidth * MiniFont.Glyphs.Length);
        Assert.Equal(MiniFont.Sheet.Size.Height, MiniFont.CellHeight);
        Assert.True(MiniFont.Outline * 2 < MiniFont.CellWidth, "描边两边加起来就占满了一格，字形没地方画");
    }

    [Fact]
    public void 字形表里每个字只出现一次()
    {
        // 出现两次的话后一格永远取不到，作者改了那一格也不会显示。
        Assert.Equal(MiniFont.Glyphs.Length, MiniFont.Glyphs.Distinct().Count());
    }

    [Fact]
    public void 挨着的两个字叠一列描边()
    {
        // 「20」两格各一圈描边，中间那一列是两个字共用的。
        Assert.Equal(MiniFont.CellWidth - MiniFont.Outline, MiniFont.XOf(1));
        Assert.Equal((MiniFont.CellWidth * 2) - MiniFont.Outline, MiniFont.WidthOf("20"));
        Assert.Equal(MiniFont.CellWidth, MiniFont.WidthOf("7"));
        Assert.Equal(0, MiniFont.WidthOf(string.Empty));
    }

    [Fact]
    public void 字形表上取格按整格挪而不按字距挪()
    {
        // 字形表上的格不叠，叠的只是排成一行的时候；按字距去取格，越往后的字取得越偏。
        for (var i = 0; i < MiniFont.Glyphs.Length; i++)
        {
            Assert.Equal(i * MiniFont.CellWidth, MiniFont.SheetXOf(MiniFont.Glyphs[i]));
        }
    }

    [Fact]
    public void 键盘那几个技能键的键名都写得出来()
    {
        // 键名画在格子角上，只能用字形表里有的字。改了默认绑定而那个键的记号写不出来，这条点名。
        var bad = InputActions.Skills
            .Select(a => InputHints.SkillLabel(a, InputDeviceKind.KeyboardMouse))
            .Where(label => label.Length == 0 || !MiniFont.CanWrite(label))
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void 件数的每一位都写得出来()
    {
        Assert.True(MiniFont.CanWrite(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void 字形表里没有的字当场抛并点名是哪个()
    {
        var error = Assert.Throws<ArgumentException>(() => MiniFont.Require("L下"));

        Assert.Contains("「下」", error.Message);
        Assert.False(MiniFont.CanWrite("下"));
        Assert.Throws<ArgumentException>(() => MiniFont.SheetXOf('下'));
    }
}
